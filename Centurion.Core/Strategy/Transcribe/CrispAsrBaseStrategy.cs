// Centurion.Core/Strategies/Transcription/CrispAsrBaseStrategy.cs

using System.Text.Json;
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.Transcribe;

/// <summary>
/// Base strategy for CrispASR with different backends (Qwen3, Whisper, etc.)
/// </summary>
public abstract class CrispAsrBaseStrategy : ITranscriptionStrategy
{
    protected readonly ToolManager _toolManager;
    protected readonly ProcessManager _processManager;
    protected readonly IModelPathResolver _modelResolver;
    protected readonly ILogger<CrispAsrBaseStrategy> _logger;

    public abstract string StrategyName { get; }

    protected CrispAsrBaseStrategy(IServiceProvider serviceProvider)
    {
        _toolManager = new ToolManager("crispasr", serviceProvider);
        _processManager = new ProcessManager(serviceProvider.GetRequiredService<ILogger<ProcessManager>>());
        _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
        _logger = serviceProvider.GetRequiredService<ILogger<CrispAsrBaseStrategy>>();
    }

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
    /// </summary>
    protected virtual string BuildArguments(string audioPath, string language, string modelPath, string? alignerPath, string? initialPrompt)
    {
        // Determine output JSON path (same base as audio)
        var jsonOutputPath = Path.ChangeExtension(audioPath, ".json");
        var jsonBasePath = Path.Combine(
            Path.GetDirectoryName(jsonOutputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(jsonOutputPath));

        var args = $"--backend {GetBackendName()} -m \"{modelPath}\" -f \"{audioPath}\" -ojf -of \"{jsonBasePath}\"";
        if (!string.IsNullOrEmpty(language))
            args += $" -l {language}";
        if (!string.IsNullOrEmpty(alignerPath))
            args += $" -am \"{alignerPath}\"";
        if (!string.IsNullOrEmpty(initialPrompt))
            args += $" --prompt \"{initialPrompt}\"";
        return args;
    }

    public async Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Ensure CrispASR tool is downloaded
        await _toolManager.EnsureToolAsync(cancellationToken);

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
        _logger.LogDebug("Executing CrispASR: {Exe} {Args}", _toolManager.ExecutablePath, args);

        // 5. Execute process
        await _processManager.ExecuteAsync(_toolManager.ExecutablePath, args, cancellationToken: cancellationToken);

        // 6. Read generated JSON
        var jsonOutputPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(jsonOutputPath))
            throw new FileNotFoundException($"CrispASR output JSON not found at: {jsonOutputPath}");

        var json = await File.ReadAllTextAsync(jsonOutputPath, cancellationToken);

        // 7. Parse
        return ParseJsonOutput(json);
    }

    /// <summary>
    /// Common JSON parser for all CrispASR backends.
    /// </summary>
    private List<Word> ParseJsonOutput(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("transcription", out var transcriptionArray))
            throw new InvalidOperationException("Missing 'transcription' array in CrispASR JSON output.");

        var words = new List<Word>();
        foreach (var segment in transcriptionArray.EnumerateArray())
        {
            if (!segment.TryGetProperty("words", out var wordArray))
                continue;

            foreach (var wordElement in wordArray.EnumerateArray())
            {
                var text = wordElement.GetProperty("text").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var offsets = wordElement.GetProperty("offsets");
                var fromMs = offsets.GetProperty("from").GetInt64();
                var toMs = offsets.GetProperty("to").GetInt64();

                words.Add(new Word
                {
                    Text = text,
                    Start = fromMs,
                    End = toMs,
                    Speaker = "SPEAKER_00"
                });
            }
        }

        if (words.Count == 0)
            _logger.LogWarning("No words were parsed from the JSON output.");

        return words;
    }
}