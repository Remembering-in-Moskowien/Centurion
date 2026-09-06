// File: Centurion.Core.PipeLine/AlignmentOp.cs
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
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
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    }

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // 1. Check if alignment is enabled
        if (!context.Config.EnableAlignment)
        {
            LogInfo("Alignment is disabled (EnableAlignment=false). Skipping.");
            context.State.AlignedSentences = context.State.SplitSentences ?? new List<Sentence>();
            context.State.IsAligned = true;
            return;
        }

        // 2. Validate input
        var sentences = context.State.SplitSentences;
        if (sentences == null || sentences.Count == 0)
        {
            LogWarning("No sentences to align. Skipping alignment.");
            context.State.AlignedSentences = new List<Sentence>();
            context.State.IsAligned = true;
            return;
        }

        var audioPath = context.State.ConvertedAudioPath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            throw new InvalidOperationException($"Audio file not found or not converted: {audioPath}");
        }

        // 3. Clear any existing word-level data before alignment
        //    This prevents duplication when the strategy adds new words.
        foreach (var sentence in sentences)
        {
            sentence.Words.Clear();
        }

        // 4. Report progress
        OnProgress(0, "Preparing alignment...");

        // 5. Get model name (fallback to default)
        var modelName = context.Config.AlignmentModel ?? "wav2vec2-base-960h";

        // 6. Create strategy via factory
        var strategy = _strategyFactory.Create(modelName);

        // 7. Execute alignment (sentences keep their original text)
        LogInfo($"Using alignment model: {modelName}");
        OnProgress(30, "Running alignment...");

        var alignedSentences = await strategy.AlignAsync(sentences, audioPath, cancellationToken);

        // 8. Update context
        context.State.AlignedSentences = alignedSentences;
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