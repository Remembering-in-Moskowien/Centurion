using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Models;
using FFMpegCore;
using SherpaOnnx;

namespace Centurion.Core.PipeLine;

/// <summary>
/// Pipeline operator for speaker diarization based on sherpa-onnx.
/// Uses a speaker embedding extractor to assign speaker labels to each word in sentences.
/// </summary>
public class DiarizationOperator : PipelineOperatorBase, IHealthCheckableOperator
{
    private readonly IModelPathResolver _pathResolver;
    private readonly double _clusterThreshold;

    public override string Name => "Speaker Diarization";

    public DiarizationOperator(IModelPathResolver pathResolver, double clusterThreshold = 0.55)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _clusterThreshold = clusterThreshold;
    }

    /// <summary>
    /// Health check: verifies FFmpeg is available and the diarization model exists.
    /// </summary>
    public override async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // Check FFmpeg
        try
        {
            await FFMpegArguments
                .FromFileInput("dummy")
                .OutputToFile("dummy", false, _ => { })
                .ProcessAsynchronously();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("FFmpeg is not properly installed or accessible.", ex);
        }
    }

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // Checkpoint: skip if already diarized
        if (context.State.IsDiarized)
        {
            LogInfo("Diarization already exists, skipping.");
            return;
        }

        // Determine input sentences: prefer split sentences, fallback to whisper sentences
        var inputSentences = context.State.SplitSentences?.Count > 0
            ? context.State.SplitSentences
            : context.State.WhisperSentences;

        if (inputSentences == null || inputSentences.Count == 0)
        {
            LogWarning("No sentences to diarize. Marking as diarized (empty).");
            context.State.DiarizedSentences = new List<Sentence>();
            context.State.IsDiarized = true;
            return;
        }

        // Get audio path
        var audioPath = context.State.PipelineTempDirectory ?? context.Config.InputFilePath;
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // Resolve model path (triggers download/validation)
        OnProgress(5, "Resolving diarization model...");
        var modelPath = await _pathResolver.GetDiarizationModelPathAsync(
            context.Config.DiarizationModel, cancellationToken);

        // Load audio samples
        OnProgress(10, "Loading audio samples...");
        float[] audioSamples;
        try
        {
            audioSamples = await LoadAudioSamplesAsync(audioPath, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError($"Failed to load audio: {ex.Message}");
            throw new DiarizationException($"Failed to load audio file: {ex.Message}", ex);
        }

        if (audioSamples == null || audioSamples.Length == 0)
            throw new DiarizationException("Audio file contains no valid data");

        var totalDurationSeconds = (double)audioSamples.Length / 16000;
        LogInfo($"Loaded {audioSamples.Length} samples, duration {totalDurationSeconds:F2}s");

        // Create extractor
        OnProgress(15, "Initializing speaker embedding extractor...");
        SpeakerEmbeddingExtractor? extractor = null;
        try
        {
            extractor = await Task.Run(() =>
            {
                var config = new SpeakerEmbeddingExtractorConfig
                {
                    Model = modelPath,
                    NumThreads = 1,
                    Debug = 0,
                    Provider = "cpu"
                };
                return new SpeakerEmbeddingExtractor(config);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new DiarizationException($"Failed to initialize sherpa-onnx extractor: {ex.Message}", ex);
        }

        // Process each sentence, extract embedding, and collect valid ones
        var validSentences = new List<Sentence>();
        var embeddings = new List<float[]>();
        var total = inputSentences.Count;
        var processed = 0;

        const int SampleRate = 16000;
        const int MinSegmentDurationMs = 200;
        const int MinSamplesForExtractor = 512; // SherpaOnnx minimum

        OnProgress(20, "Extracting speaker embeddings...");

        foreach (var sentence in inputSentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startMs = Math.Max(0, sentence.Start);
            var endMs = Math.Min(sentence.End, totalDurationSeconds * 1000.0);
            if (startMs >= endMs)
            {
                LogWarning($"Skipping sentence with invalid timestamps: start={sentence.Start}, end={sentence.End}");
                continue;
            }

            var durationMs = endMs - startMs;
            if (durationMs < MinSegmentDurationMs)
            {
                LogWarning($"Skipping too short segment: {durationMs:F1}ms");
                continue;
            }

            var segment = ExtractSegment(audioSamples, startMs, endMs, SampleRate);
            if (segment == null || segment.Length < MinSamplesForExtractor)
            {
                LogWarning($"Segment too short for extractor: {segment?.Length ?? 0} samples");
                continue;
            }

            float[] embedding;
            try
            {
                using var stream = extractor.CreateStream();
                stream.AcceptWaveform(SampleRate, segment);
                if (!extractor.IsReady(stream))
                {
                    LogWarning($"Extractor not ready for segment ({startMs}-{endMs})");
                    continue;
                }

                embedding = extractor.Compute(stream);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to extract embedding for segment ({startMs}-{endMs}): {ex.Message}");
                continue;
            }

            if (embedding == null || embedding.Length == 0)
            {
                LogWarning($"Empty embedding for segment ({startMs}-{endMs})");
                continue;
            }

            validSentences.Add(sentence);
            embeddings.Add(embedding);

            processed++;
            var percent = 20 + (int)((double)processed / total * 70);
            OnProgress(percent, $"Processed {processed}/{total} sentences...");
        }

        // Clean up extractor
        extractor?.Dispose();

        if (validSentences.Count == 0)
        {
            LogWarning("No valid sentences for diarization. Marking as diarized (empty).");
            context.State.DiarizedSentences = new List<Sentence>();
            context.State.IsDiarized = true;
            return;
        }

        OnProgress(90, "Clustering speaker embeddings...");
        LogInfo($"Extracted embeddings for {validSentences.Count} segments");

        // Cluster
        var labels = ClusterEmbeddings(embeddings, _clusterThreshold);

        // Assign speaker labels
        for (var i = 0; i < validSentences.Count; i++)
        {
            var speakerId = labels[i];
            var speakerLabel = $"Speaker{speakerId}";
            foreach (var word in validSentences[i].Words) word.Speaker = speakerLabel;
        }

        // Store result
        context.State.DiarizedSentences = validSentences;
        context.State.IsDiarized = true;

        OnProgress(100, $"Diarization completed. Assigned {labels.Distinct().Count()} speakers.");
        LogInfo($"Diarized {validSentences.Count} sentences.");
    }

    /// <summary>
    /// Loads audio as 16kHz mono 16-bit PCM samples normalized to [-1, 1].
    /// </summary>
    private async Task<float[]> LoadAudioSamplesAsync(string filePath, CancellationToken ct)
    {
        var tempPcmFile = Path.Combine(Path.GetTempPath(), $"diarizor_{Guid.NewGuid():N}.pcm");
        try
        {
            await FFMpegArguments
                .FromFileInput(filePath)
                .OutputToFile(tempPcmFile, true, options => options
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-ac 1")
                    .WithCustomArgument("-f s16le"))
                .ProcessAsynchronously();

            if (!File.Exists(tempPcmFile))
                throw new DiarizationException("Temporary PCM file was not created.");

            var bytes = await File.ReadAllBytesAsync(tempPcmFile, ct);
            if (bytes.Length == 0)
                throw new DiarizationException("PCM file is empty.");

            if (bytes.Length % 2 != 0)
                throw new DiarizationException("Odd number of bytes in PCM data; file may be corrupted.");

            var sampleCount = bytes.Length / 2;
            var floats = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = BitConverter.ToInt16(bytes, i * 2);
                floats[i] = sample / 32768.0f;
            }

            return floats;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DiarizationException($"FFmpeg decoding or file reading failed: {ex.Message}", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPcmFile)) File.Delete(tempPcmFile);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private float[]? ExtractSegment(float[] fullSamples, double startMs, double endMs, int sampleRate)
    {
        var startSample = (int)(startMs * sampleRate / 1000.0);
        var endSample = (int)(endMs * sampleRate / 1000.0);
        if (startSample < 0) startSample = 0;
        if (endSample > fullSamples.Length) endSample = fullSamples.Length;
        if (startSample >= endSample) return null;

        var length = endSample - startSample;
        var segment = new float[length];
        Array.Copy(fullSamples, startSample, segment, 0, length);
        return segment;
    }

    private List<int> ClusterEmbeddings(List<float[]> embeddings, double threshold)
    {
        if (embeddings.Count == 0) return [];

        var embeddingLength = embeddings[0].Length;
        if (embeddings.Any(e => e.Length != embeddingLength))
            throw new DiarizationException("Embedding vectors have inconsistent lengths");

        var labels = new List<int> { 0 };
        var clusterCenters = new List<float[]> { embeddings[0] };

        for (var i = 1; i < embeddings.Count; i++)
        {
            var emb = embeddings[i];
            var assigned = false;
            for (var c = 0; c < clusterCenters.Count; c++)
            {
                var sim = CosineSimilarity(emb, clusterCenters[c]);
                if (sim >= threshold)
                {
                    labels.Add(c);
                    var center = clusterCenters[c];
                    var count = labels.Count(l => l == c);
                    for (var j = 0; j < center.Length; j++)
                        center[j] = (center[j] * (count - 1) + emb[j]) / count;
                    assigned = true;
                    break;
                }
            }

            if (!assigned)
            {
                clusterCenters.Add(emb);
                labels.Add(clusterCenters.Count - 1);
            }
        }

        return labels;
    }

    private double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vector length mismatch");
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}