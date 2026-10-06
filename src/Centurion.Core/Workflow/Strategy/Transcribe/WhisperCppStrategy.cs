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
/// Transcription strategy based on whisper.cpp: invokes the whisper.cpp executable to transcribe audio,
/// then parses its JSON output to extract word-level timestamps.
/// </summary>
public class WhisperCppStrategy(IServiceProvider serviceProvider) : ITranscriptionStrategy
{
    private readonly IToolManagerFactory _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
    private readonly ProcessManager _processManager = serviceProvider.GetRequiredService<ProcessManager>();
    private readonly IModelPathResolver _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
    private readonly ILogger<WhisperCppStrategy> _logger = serviceProvider.GetRequiredService<ILogger<WhisperCppStrategy>>();
    private ToolManager? _toolManager;

    /// <summary>Display name of the strategy.</summary>
    public string StrategyName => "Whisper.cpp";

    /// <summary>Whisper.cpp emits word timestamps from the decoder; they are not forced-aligned to phonemes, so no capability is declared.</summary>
    public StrategyCapabilities Capabilities => StrategyCapabilities.None;

    /// <summary>Creates (lazily) the whisper.cpp tool manager for the inference device (auto-selects the CUDA build when a GPU is available).</summary>
    private ToolManager GetToolManager(InferenceDevice device) =>
        _toolManager ??= _toolManagerFactory.Create("whispercpp", device);

    /// <summary>
    /// Performs transcription: ensures the whisper.cpp tool is ready, resolves the model, builds and runs the CLI,
    /// then reads the output JSON to extract word-level timestamps.
    /// </summary>
    /// <param name="audioPath">Path to the audio file to transcribe.</param>
    /// <param name="language">Audio language code.</param>
    /// <param name="modelName">Transcription model name.</param>
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
        // 1. Ensure the tool is downloaded (GPU/CPU variant chosen by device)
        var toolManager = GetToolManager(device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Resolve the model file path
        var modelPath = await _modelResolver.GetWhisperModelPathAsync(modelName, cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model file not found: {modelPath}");

        // 3. Build the argument array: use -oj to force JSON output; the output file is auto-generated next to the audio
        //    When language is empty, omit -l so the model auto-detects it (more robust for non-English audio such as Chinese)
        //    Pass via ArgumentList so quotes in paths or prompts do not break the argument boundaries
        //    Note: whisper.cpp v1.9.x only writes the JSON file when -of is given; without it the JSON is only printed to stdout.
        var jsonBase = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? Directory.GetCurrentDirectory(),
            Path.GetFileNameWithoutExtension(audioPath));
        var args = new List<string> { "-m", modelPath, "-f", audioPath, "-oj", "-of", jsonBase };
        if (!string.IsNullOrWhiteSpace(language))
        {
            args.Add("-l");
            args.Add(language);
        }
        if (!string.IsNullOrEmpty(initialPrompt))
        {
            args.Add("-p");
            args.Add(initialPrompt);
        }

        _logger.LogDebug("Executing: {Exe} {Args}", toolManager.ExecutablePath, string.Join(' ', args));

        // 4. Run the process (it produces a JSON file; stdout may only be progress or logs)
        var output = await _processManager.ExecuteAsync(
            toolManager.ExecutablePath,
            args,
            cancellationToken: cancellationToken);

        // 5. Determine the JSON file path (same directory and base name as the audio, extension .json)
        var jsonPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"Whisper.cpp did not produce expected JSON file: {jsonPath}");

        // 6. Read and parse the JSON
        var jsonContent = File.ReadAllText(jsonPath);
        var whisperResult = JsonParser.Deserialize<WhisperTranscriptJson>(jsonContent);

        // 7. Extract word-level information
        return ExtractWords(whisperResult);
    }

    /// <summary>
    /// Extracts word-level timestamps from WhisperTranscriptJson.
    /// Prefers token-level word information (most precise); otherwise falls back to the segment level and splits by whitespace (losing precise timing).
    /// </summary>
    private List<Word> ExtractWords(WhisperTranscriptJson result)
    {
        var words = new List<Word>();

        foreach (var item in result.Transcription)
        {
            // Prefer tokens (word-level)
            if (item.Tokens != null && item.Tokens.Count > 0)
            {
                foreach (var token in item.Tokens)
                {
                    var text = token.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(text))
                        continue;

                    // Skip special markers (e.g. blanks, punctuation); filtering can be adjusted as needed
                    // Here we keep all non-empty tokens
                    words.Add(new Word
                    {
                        Text = text,
                        Start = token.Offsets.From / 1000.0,
                        End = token.Offsets.To / 1000.0,
                        Speaker = "SPEAKER_00" // filled later by diarization
                    });
                }
            }
            else
            {
                // Fallback: use the segment level, split the text into words by spaces, and spread the time evenly (imprecise, not recommended)
                var segmentText = item.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(segmentText))
                    continue;

                var parts = segmentText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                var segmentStart = item.Offsets.From / 1000.0;
                var segmentEnd = item.Offsets.To / 1000.0;
                var duration = segmentEnd - segmentStart;
                var avgDuration = duration / parts.Length;

                for (var i = 0; i < parts.Length; i++)
                {
                    words.Add(new Word
                    {
                        Text = parts[i],
                        Start = segmentStart + i * avgDuration,
                        End = segmentStart + (i + 1) * avgDuration,
                        Speaker = "SPEAKER_00"
                    });
                }
            }
        }

        return words;
    }
}
