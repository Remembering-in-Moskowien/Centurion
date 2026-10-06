using Centurion.Core.Workflow.Factories;using Centurion.Models.Workflow;

using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Transcript;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Strategy.Transcribe;

/// <summary>
/// Base strategy for CrispASR with different backends (Qwen3, Whisper, etc.)
/// </summary>
public abstract class CrispAsrBaseStrategy : ITranscriptionStrategy
{
    /// <summary>Factory that creates the CrispASR tool manager per device.</summary>
    protected readonly IToolManagerFactory _toolManagerFactory;
    /// <summary>Executor that starts and manages external CLI processes.</summary>
    protected readonly ProcessManager _processManager;
    /// <summary>Resolver for the local paths of model files.</summary>
    protected readonly IModelPathResolver _modelResolver;
    /// <summary>Logger for recording the transcription process.</summary>
    protected readonly ILogger<CrispAsrBaseStrategy> _logger;
    private ToolManager? _toolManager;

    /// <summary>Display name of the strategy.</summary>
    public abstract string StrategyName { get; }

    /// <summary>Base transcription strategies output plain word timestamps; subclasses that attach a forced aligner declare <see cref="StrategyCapabilities.AlignedTimestamps"/>.</summary>
    public virtual StrategyCapabilities Capabilities => StrategyCapabilities.None;

    /// <summary>Resolves the required services from the dependency injection container and initializes the shared base dependencies.</summary>
    /// <param name="serviceProvider">Container used to resolve the tool factory, process manager, model resolver, and logger.</param>
    protected CrispAsrBaseStrategy(IServiceProvider serviceProvider)
    {
        _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
        _processManager = serviceProvider.GetRequiredService<ProcessManager>();
        _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
        _logger = serviceProvider.GetRequiredService<ILogger<CrispAsrBaseStrategy>>();
    }

    /// <summary>Creates (lazily) the CrispASR tool manager for the given inference device.</summary>
    protected ToolManager GetToolManager(InferenceDevice device) =>
        _toolManager ??= _toolManagerFactory.Create("crispasr", device);

    /// <summary>
    /// Backend name to pass to CrispASR (e.g., "qwen3", "whisper")
    /// </summary>
    protected abstract string GetBackendName();

    /// <summary>
    /// Resolve the model file path for the given model name.
    /// </summary>
    protected abstract Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken);

    /// <summary>
    /// Optionally resolve an aligner model path; return null if not used.
    /// </summary>
    protected virtual Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Build the command-line arguments. Override if needed.
    /// Returns the argument list (without quoting), safely passed by <see cref="ProcessManager"/> as an ArgumentList,
    /// so that quotes in paths or prompts do not break the argument boundaries.
    /// </summary>
    protected virtual IReadOnlyList<string> BuildArguments(string audioPath, string language, string modelPath, string? alignerPath, string? initialPrompt)
    {
        // Determine output JSON base path (same base as audio)
        var jsonOutputPath = Path.ChangeExtension(audioPath, ".json");
        var jsonBasePath = Path.Combine(
            Path.GetDirectoryName(jsonOutputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(jsonOutputPath));

        var args = new List<string>
        {
            "--backend", GetBackendName(),
            "-m", modelPath,
            "-f", audioPath,
            "-ojf",
            "-of", jsonBasePath
        };
        if (!string.IsNullOrEmpty(language))
        {
            args.Add("-l");
            args.Add(language);
        }
        if (!string.IsNullOrEmpty(alignerPath))
        {
            args.Add("-am");
            args.Add(alignerPath);
        }
        if (!string.IsNullOrEmpty(initialPrompt))
        {
            args.Add("--prompt");
            args.Add(initialPrompt);
        }
        return args;
    }

    /// <summary>
    /// Performs transcription: ensures CrispASR is ready, resolves the model and optional aligner, builds and
    /// runs the CLI, then parses the output JSON into a list of word-level timestamps.
    /// </summary>
    /// <param name="audioPath">Path to the audio file to transcribe.</param>
    /// <param name="language">Audio language code; when empty the model auto-detects it.</param>
    /// <param name="modelName">Transcription model name; when empty the implementation's default model is used.</param>
    /// <param name="initialPrompt">Optional initial prompt.</param>
    /// <param name="cancellationToken">Token used to cancel the transcription process.</param>
    /// <param name="device">Inference device, which selects the CPU/GPU tool variant.</param>
    public async Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto)
    {
        // 1. Ensure the CrispASR tool is downloaded (the GPU variant is auto-selected by device)
        var toolManager = GetToolManager(device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Get model path
        var modelPath = await GetModelPathAsync(modelName, cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");

        // 3. Get aligner if available
        var alignerPath = await GetAlignerPathAsync(cancellationToken);
        if (alignerPath != null && !File.Exists(alignerPath))
        {
            _logger.LogWarning("Aligner model not found at {Path}. Alignment will be skipped.", alignerPath);
            alignerPath = null;
        }

        // 4. Build arguments
        var args = BuildArguments(audioPath, language, modelPath, alignerPath, initialPrompt);
        _logger.LogDebug("Executing CrispASR: {Exe} {Args}", toolManager.ExecutablePath, string.Join(' ', args));

        // 5. Execute process
        await _processManager.ExecuteAsync(toolManager.ExecutablePath, args, cancellationToken: cancellationToken);

        // 6. Read generated JSON
        var jsonOutputPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(jsonOutputPath))
            throw new FileNotFoundException($"CrispASR output JSON not found at: {jsonOutputPath}");

        var json = await File.ReadAllTextAsync(jsonOutputPath, cancellationToken);

        // 7. Parse
        return ParseJsonOutput(json);
    }

    /// <summary>
    /// Parses CrispASR output JSON (entity-model deserialization) and extracts the word-level timestamps.
    /// Compatible with both the whisper and qwen3 backends. The qwen3 word-level timestamps come from a forced
    /// aligner, and on long audio "alignment collapse" can occur (many zero-duration words, incomplete in-segment
    /// coverage); in that case it falls back to segment-level interpolation: time is allocated within the segment
    /// [From,To] proportionally to each word's length. Segment-level timestamps (CrispASR chunks by audio) are proven reliable.
    /// </summary>
    /// <param name="json">The CrispASR -ojf format JSON string.</param>
    /// <returns>The list of word-level timestamps after parsing.</returns>
    private List<Word> ParseJsonOutput(string json)
    {
        var root = JsonParser.Deserialize<CrispAsrTranscriptJson>(json);

        if (root.Transcription is null)
            throw new InvalidOperationException("Missing 'transcription' array in CrispASR JSON output.");

        var words = new List<Word>();
        foreach (var segment in root.Transcription)
        {
            // Use word-level timestamps when healthy; otherwise (high zero-duration ratio / incomplete coverage) fall back to segment interpolation
            var segmentWords = ParseSegmentWords(segment);
            if (segmentWords.Count > 0 && IsWordTimingHealthy(segmentWords, segment))
            {
                words.AddRange(segmentWords);
            }
            else
            {
                words.AddRange(InterpolateSegmentWords(segment));
            }
        }

        if (words.Count == 0)
            _logger.LogWarning("No words were parsed from the JSON output.");

        return words;
    }

    /// <summary>
    /// Extracts words from the segment's words array (skipping blank text).
    /// </summary>
    /// <param name="segment">One transcription segment from the CrispASR output.</param>
    /// <returns>The word list of this segment (timestamps not yet validated).</returns>
    private static List<Word> ParseSegmentWords(CrispAsrTranscriptionItem segment)
    {
        var result = new List<Word>();
        if (segment.Words is null)
            return result;

        foreach (var wordElement in segment.Words)
        {
            var text = wordElement.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            result.Add(new Word
            {
                Text = text.Trim(),
                Start = wordElement.Offsets.From,
                End = wordElement.Offsets.To,
                Speaker = "SPEAKER_00"
            });
        }

        return result;
    }

    /// <summary>
    /// Determines whether the segment's word-level timestamps are healthy: zero-duration (or negative-duration) words
    /// stay under a threshold ratio, and the word time range basically covers the segment range (poor tail coverage means alignment collapse).
    /// </summary>
    /// <param name="segmentWords">The word list of this segment.</param>
    /// <param name="segment">The corresponding transcription segment (provides segment-level timestamps).</param>
    /// <returns>true when the word-level timestamps are trustworthy.</returns>
    private static bool IsWordTimingHealthy(IReadOnlyList<Word> segmentWords, CrispAsrTranscriptionItem segment)
    {
        if (segmentWords.Count == 0)
            return false;

        var zeroDuration = 0;
        foreach (var w in segmentWords)
        {
            if (w.End <= w.Start)
                zeroDuration++;
        }

        var segmentDuration = segment.Offsets.To - segment.Offsets.From;
        var covered = segmentWords[^1].End - segmentWords[0].Start;
        var coverageRatio = segmentDuration > 0 ? covered / (double)segmentDuration : 0.0;

        // Healthy when zero-duration words are ≤ 20% and words cover ≥ 80% of the segment duration
        return zeroDuration / (double)segmentWords.Count <= 0.2 && coverageRatio >= 0.8;
    }

    /// <summary>
    /// Segment-level interpolation: splits the segment text into words by whitespace (keeping punctuation),
    /// and linearly allocates time within the segment [From,To] by each word's share of the total text length.
    /// Guarantees a monotonic timeline, no zero durations, and full coverage of the segment.
    /// </summary>
    /// <param name="segment">One transcription segment from the CrispASR output.</param>
    /// <returns>The interpolated word list.</returns>
    private static List<Word> InterpolateSegmentWords(CrispAsrTranscriptionItem segment)
    {
        var result = new List<Word>();
        var text = segment.Text;
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return result;

        var from = segment.Offsets.From;
        var to = segment.Offsets.To;
        var totalLength = tokens.Sum(t => t.Length);
        if (totalLength <= 0)
            return result;

        var cursor = (double)from;
        foreach (var token in tokens)
        {
            var duration = (double)(to - from) * token.Length / totalLength;
            var end = cursor + duration;
            result.Add(new Word
            {
                Text = token,
                Start = (long)cursor,
                End = (long)end,
                Speaker = "SPEAKER_00"
            });
            cursor = end;
        }

        return result;
    }
}
