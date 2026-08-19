using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Metadata;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Centurion.Core.Tools;
using SherpaOnnx;

namespace Centurion.Core.Operators;

/// <summary>
/// 使用 sherpa-onnx (wav2vec2 CTC) 的说话人感知强制对齐算子。
/// 流程：按说话人分组 → 每个句子单独提取音频片段 → 调用 sherpa-onnx OfflineRecognizer 进行 CTC 对齐 → 时间插值映射回原始句子。
/// 所有时间单位均为毫秒。
/// </summary>
public class TimeStampAlignmentOperator
    : IOperator<SpeakerMfaRequest, SpeakerMfaResponse>, IAsyncDisposable
{
    private readonly ITempDirectoryManager _tempManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _modelName;
    private readonly string _categoryFolder;
    private OfflineRecognizer? _recognizer;
    private bool _isReady;
    private bool _disposed;

    private const int SampleRate = 16000;

    public TimeStampAlignmentOperator(
        ITempDirectoryManager tempManager,
        IServiceProvider serviceProvider,
        string modelName = "wav2vec2-base-960h",
        string categoryFolder = "alignment")
    {
        _tempManager = tempManager ?? throw new ArgumentNullException(nameof(tempManager));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _modelName = modelName;
        _categoryFolder = categoryFolder;
    }

    public async Task EnsureTargetAvailableAsync()
    {
        if (_isReady) return;

        // 1. 通过 ModelManager 获取模型目录
        var modelManager = new ModelManager(
            _modelName,
            ModelRegistry.Wav2Vec2Models,
            _serviceProvider,
            _categoryFolder);
        await modelManager.EnsureModelAvailableAsync();

        var modelDir = modelManager.ModelFilePath;
        var modelPath = Path.Combine(modelDir, "model.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"model.onnx not found in {modelDir}");
        if (!File.Exists(tokensPath))
            throw new FileNotFoundException($"tokens.txt not found in {modelDir}");

        // 2. 配置 OfflineRecognizer
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.ZipformerCtc.Model = modelPath;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.NumThreads = 1;
        config.DecodingMethod = "greedy_search";
        config.ModelConfig.Debug = 1;

        _recognizer = new OfflineRecognizer(config);
        _isReady = true;

        ConsoleServices.Output?.WriteLine("sherpa-onnx OfflineRecognizer initialized with Wav2Vec2 CTC model.");
        await Task.CompletedTask;
    }

    public async Task<SpeakerMfaResponse> ProcessAsync(
        OperatorsRequest<SpeakerMfaRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        var payload = request.Payload;
        return await AlignAsync(payload, cancellationToken);
    }

    private async Task<SpeakerMfaResponse> AlignAsync(SpeakerMfaRequest payload, CancellationToken ct)
    {
        var sentences = payload.Sentences;
        if (sentences == null || sentences.Count == 0)
            return new SpeakerMfaResponse { Sentences = [] };

        // 获取音频总时长（毫秒）
        var totalDurationMs = await GetAudioDurationAsync(payload.AudioFilePath, ct);
        ConsoleServices.Output?.WriteLine($"Audio total duration: {totalDurationMs:F0} ms ({totalDurationMs / 1000.0:F2} s).");

        // 按说话人分组
        var speakerGroups = sentences
            .Select((s, idx) => new { Sentence = s, Index = idx })
            .GroupBy(x => x.Sentence.Words.FirstOrDefault()?.Speaker ?? "unknown")
            .ToDictionary(g => g.Key, g => g.ToList());

        var globalWords = sentences.SelectMany(s => s.Words).ToList();
        var allMfaTimings = new List<(double Start, double End)>();

        await using var corpusRoot = await _tempManager.CreateTempDirectoryAsync("sherpa_align_");
        var corpusDir = corpusRoot.Path;

        foreach (var group in speakerGroups)
        {
            var speaker = group.Key;
            var safeSpeaker = string.Concat(speaker.Where(c => char.IsLetterOrDigit(c) || c == '_'));
            if (string.IsNullOrEmpty(safeSpeaker)) safeSpeaker = "unknown";

            var speakerDir = Path.Combine(corpusDir, safeSpeaker);
            Directory.CreateDirectory(speakerDir);

            // 按时间排序
            var speakerSentences = group.Value.OrderBy(x => x.Sentence.Start).Select(x => x.Sentence).ToList();

            ConsoleServices.Output?.WriteLine($"Speaker '{speaker}': {speakerSentences.Count} sentences.");

            for (int i = 0; i < speakerSentences.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sentence = speakerSentences[i];
                var segName = $"sent_{i + 1:D4}";

                // 截断结束时间，防止超出音频总时长
                var originalStartMs = sentence.Start;
                var originalEndMs = sentence.End;
                var safeEndMs = Math.Min(originalEndMs, totalDurationMs);
                if (safeEndMs <= originalStartMs)
                {
                    ConsoleServices.Output?.WriteLine($"Warning: Sentence {i + 1} end ({originalEndMs}ms) is before or equal start ({originalStartMs}ms). Skipping.");
                    continue;
                }
                if (safeEndMs != originalEndMs)
                {
                    ConsoleServices.Output?.WriteLine($"Sentence {i + 1} end truncated from {originalEndMs}ms to {safeEndMs}ms (audio duration limit).");
                }

                ConsoleServices.Output?.WriteLine($"Processing sentence {i + 1}/{speakerSentences.Count}: start={originalStartMs}ms, end={safeEndMs}ms, words={sentence.Words.Count}");

                var segAudioPath = Path.Combine(speakerDir, $"{segName}.wav");
                await ExtractAudioSegmentAsync(
                    payload.AudioFilePath, originalStartMs, safeEndMs, segAudioPath, ct);

                var fileInfo = new FileInfo(segAudioPath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    ConsoleServices.Output?.WriteLine($"Warning: Extracted audio file is empty or missing: {segAudioPath}. Skipping this sentence.");
                    continue;
                }

                var text = string.Join(" ", sentence.Words.Select(w => w.Text));
                var timings = await AlignSegmentWithRecognizerAsync(segAudioPath, text, ct);
                allMfaTimings.AddRange(timings);
            }
        }

        // 全局时间插值（将每个句子的对齐结果映射回原始词列表）
        ApplyTimeInterpolation(globalWords, allMfaTimings);

        // 更新每个句子的起止时间（毫秒）
        foreach (var sentence in sentences)
        {
            if (sentence.Words.Count > 0)
            {
                sentence.Start = sentence.Words.Min(w => w.Start);
                sentence.End = sentence.Words.Max(w => w.End);
            }
        }

        return new SpeakerMfaResponse { Sentences = sentences };
    }

    /// <summary>
    /// 使用 OfflineRecognizer 进行 CTC 强制对齐，获取词级时间戳（返回秒）。
    /// </summary>
    private async Task<List<(double Start, double End)>> AlignSegmentWithRecognizerAsync(
        string audioPath, string text, CancellationToken ct)
    {
        if (_recognizer == null)
            throw new InvalidOperationException("Recognizer not initialized.");

        var audioData = await ReadAudioAsFloatAsync(audioPath, ct);
        if (audioData.Any(float.IsNaN) || audioData.Any(float.IsInfinity))
            throw new InvalidOperationException("Audio data contains invalid values.");

        // 诊断信息
        ConsoleServices.Output?.WriteLine($"Audio samples: {audioData.Length}, max={audioData.Max():F4}, min={audioData.Min():F4}, avg={audioData.Average():F4}");
        var totalDuration = audioData.Length / (double)SampleRate;
        ConsoleServices.Output?.WriteLine($"Text length: {text.Length} characters.");

        var stream = _recognizer.CreateStream();
        try
        {
            stream.AcceptWaveform(SampleRate, audioData);
            _recognizer.Decode(stream);
            var result = stream.Result;

            var timings = new List<(double Start, double End)>();
            if (result?.Tokens != null && result.Timestamps != null && result.Tokens.Length == result.Timestamps.Length)
            {
                for (int i = 0; i < result.Tokens.Length; i++)
                {
                    var token = result.Tokens[i];
                    if (!string.IsNullOrWhiteSpace(token) && token != "<unk>" && token != "▁")
                    {
                        var start = result.Timestamps[i];
                        var end = (i + 1 < result.Timestamps.Length) ? result.Timestamps[i + 1] : totalDuration;
                        if (end - start < 0.001) end = start + 0.01;
                        timings.Add((start, end));
                    }
                }
            }

            if (timings.Count == 0)
                ConsoleServices.Output?.WriteLine($"Warning: No valid timings extracted from {audioPath}.");

            return timings;
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// <summary>
    /// 将 WAV 文件读取为 16kHz 单声道 float 数组。
    /// </summary>
    private async Task<float[]> ReadAudioAsFloatAsync(string audioPath, CancellationToken ct)
    {
        var tempRaw = Path.GetTempFileName();
        try
        {
            var args = $"-y -i \"{audioPath}\" -ac 1 -ar {SampleRate} -f f32le -acodec pcm_f32le \"{tempRaw}\"";
            var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg", args)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Failed to start FFmpeg.");

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"FFmpeg conversion failed: {error}");
            }

            var fileInfo = new FileInfo(tempRaw);
            if (!fileInfo.Exists || fileInfo.Length == 0)
                throw new InvalidOperationException("FFmpeg produced empty output.");

            var bytes = await File.ReadAllBytesAsync(tempRaw, ct);
            if (bytes.Length % 4 != 0)
                throw new InvalidOperationException($"Invalid float data length: {bytes.Length}");

            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }
        finally
        {
            if (File.Exists(tempRaw)) File.Delete(tempRaw);
        }
    }

    /// <summary>
    /// 获取音频总时长（毫秒）。
    /// </summary>
    private async Task<double> GetAudioDurationAsync(string filePath, CancellationToken ct)
    {
        var args = $"-i \"{filePath}\" -f null -";
        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg", args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start FFmpeg for duration probing.");

        var errorOutput = new StringBuilder();
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorOutput.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        var output = errorOutput.ToString();
        var match = Regex.Match(output, @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2,3})");
        if (!match.Success)
            throw new Exception("Could not parse audio duration from ffprobe output.");

        var hours = int.Parse(match.Groups[1].Value);
        var minutes = int.Parse(match.Groups[2].Value);
        var seconds = int.Parse(match.Groups[3].Value);
        var milliseconds = int.Parse(match.Groups[4].Value.PadRight(3, '0'));

        return new TimeSpan(0, hours, minutes, seconds, milliseconds).TotalMilliseconds;
    }

    /// <summary>
    /// 截取音频片段（直接剪切，无静音填充）。
    /// </summary>
    private async Task ExtractAudioSegmentAsync(
        string inputPath,
        double startMs,
        double endMs,
        string outputPath,
        CancellationToken ct)
    {
        var startSec = (startMs / 1000.0).ToString(CultureInfo.InvariantCulture);
        var endSec = (endMs / 1000.0).ToString(CultureInfo.InvariantCulture);

        var args = $"-y -i \"{inputPath}\" -ss {startSec} -to {endSec} " +
                   $"-ac 1 -ar {SampleRate} -acodec pcm_s16le \"{outputPath}\"";

        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg", args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start FFmpeg.");

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"FFmpeg extraction failed: {error}");
        }

        var fileInfo = new FileInfo(outputPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
            throw new InvalidOperationException($"Extraction produced empty file: {outputPath}");
    }

    /// <summary>
    /// 时间插值：将对齐结果（秒）映射到原始词列表（毫秒）。
    /// </summary>
    private void ApplyTimeInterpolation(List<Word> newWords, List<(double Start, double End)> oldTimings)
    {
        if (oldTimings.Count == 0 || newWords.Count == 0)
            return;

        if (newWords.Count == oldTimings.Count)
        {
            for (int i = 0; i < newWords.Count; i++)
            {
                newWords[i].Start = oldTimings[i].Start * 1000.0;
                newWords[i].End = oldTimings[i].End * 1000.0;
            }
            return;
        }

        var oldBoundaries = new double[oldTimings.Count + 1];
        oldBoundaries[0] = oldTimings[0].Start;
        for (int i = 0; i < oldTimings.Count; i++)
            oldBoundaries[i + 1] = oldTimings[i].End;
        oldBoundaries[oldTimings.Count] = oldTimings[oldTimings.Count - 1].End;

        var totalDuration = oldBoundaries[^1] - oldBoundaries[0];
        if (totalDuration <= 0) return;

        var oldNorm = oldBoundaries.Select(b => (b - oldBoundaries[0]) / totalDuration).ToArray();

        var newNorm = new double[newWords.Count + 1];
        newNorm[0] = 0;
        newNorm[^1] = 1;
        for (int i = 1; i < newWords.Count; i++)
        {
            double t = (double)i / newWords.Count;
            newNorm[i] = Interpolate(oldNorm, t);
        }

        var timeStart = oldBoundaries[0];
        for (int i = 0; i < newWords.Count; i++)
        {
            newWords[i].Start = (timeStart + newNorm[i] * totalDuration) * 1000.0;
            newWords[i].End = (timeStart + newNorm[i + 1] * totalDuration) * 1000.0;
        }
    }

    private double Interpolate(double[] oldNorm, double t)
    {
        if (t <= 0) return oldNorm[0];
        if (t >= 1) return oldNorm[^1];

        int n = oldNorm.Length - 1;
        for (int i = 0; i < n; i++)
        {
            if (t >= oldNorm[i] && t <= oldNorm[i + 1])
            {
                double localT = (t - oldNorm[i]) / (oldNorm[i + 1] - oldNorm[i]);
                return oldNorm[i] + localT * (oldNorm[i + 1] - oldNorm[i]);
            }
        }
        return oldNorm[^1];
    }

    // ---------- 资源释放 ----------
    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
        await Task.CompletedTask;
    }
}