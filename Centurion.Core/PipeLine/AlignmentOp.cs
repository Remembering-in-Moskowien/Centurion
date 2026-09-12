// File: Centurion.Core.PipeLine/AlignmentOp.cs
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Exceptions;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

/// <summary>
/// Pipeline operator that performs forced alignment on sentences.
/// It reads the split sentences and converted audio path from the workflow context,
/// executes the configured alignment strategy (via factory) to refine word-level timestamps,
/// and writes the result back to State.AlignedSentences.
/// </summary>
public class AlignmentOp : PipelineOperatorBase, IHealthCheckableOperator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlignmentOp> _logger;
    private readonly IAlignmentStrategyFactory _strategyFactory;

    public override string Name => "Forced Alignment";

    public AlignmentOp(
        IServiceProvider serviceProvider,
        ILogger<AlignmentOp> logger,
        IAlignmentStrategyFactory strategyFactory)
        : base(logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    }

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // 1. Check if alignment is enabled
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0)
        {
            const string message = "No current sentences are available for alignment.";
            context.State.Errors.Add(message);
            _logger.LogError(message);
            throw new AlignmentException(message);
        }

        if (!context.Config.EnableAlignment)
        {
            LogInfo("Alignment is disabled (EnableAlignment=false). Skipping.");
            context.State.IsAligned = false;
            return;
        }

        // 2. Validate input
        var audioPath = context.State.ConvertedAudioPath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            throw new InvalidOperationException($"Audio file not found or not converted: {audioPath}");
        }

        // Report progress.
        OnProgress(0, "Preparing alignment...");

        // 5. Get model name (fallback to default)
        var modelName = context.Config.AlignmentModel ?? "wav2vec2-base-960h";

        // 6. Create strategy via factory
        var strategy = _strategyFactory.Create(modelName);

        // 7. Execute alignment (sentences keep their original text)
        LogInfo($"Using alignment model: {modelName}");
        OnProgress(30, "Running alignment...");

        var alignedSentences = await strategy.AlignAsync(sentences, audioPath, cancellationToken);
        if (alignedSentences is null || alignedSentences.Count != sentences.Count)
        {
            var actualCount = alignedSentences?.Count ?? 0;
            var message = $"Alignment changed the sentence count from {sentences.Count} to {actualCount}.";
            _logger.LogError(message);
            context.State.Errors.Add(message);
            throw new AlignmentException(message);
        }

        // 8. Update context
        context.State.AlignedSentences = alignedSentences;
        context.State.CurrentSentences = context.State.AlignedSentences;
        context.State.IsAligned = true;

        OnProgress(100, "Alignment completed");
        LogInfo($"Alignment finished. Processed {alignedSentences.Count} sentences.");
    }

    public override async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        LogInfo("Checking alignment environment...");

        var toolManager = new ToolManager("crispasr", _serviceProvider);
        if (!File.Exists(toolManager.ExecutablePath))
        {
            LogWarning($"CrispASR tool not found at: {toolManager.ExecutablePath}");
        }
        else
        {
            LogInfo($"CrispASR tool found at: {toolManager.ExecutablePath}");
        }

        await Task.CompletedTask;
    }
}