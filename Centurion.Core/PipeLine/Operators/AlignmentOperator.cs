using Centurion.Core.Factories;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Exceptions;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// Pipeline operator that performs forced alignment on sentences.
/// It reads the current subtitle sentences and converted audio path from the workflow context,
/// executes the configured alignment strategy (via factory) to refine word-level timestamps,
/// and writes the result back to State.AlignedSentences and State.CurrentSentences.
/// </summary>
public class AlignmentOperator : PipelineOperatorBase<AlignmentOperator>, IHealthCheckableOperator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlignmentOperator> _logger;
    private readonly IAlignmentStrategyFactory _strategyFactory;
    private readonly IToolManagerFactory _toolManagerFactory;

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Forced Alignment";

    /// <summary>
    /// 创建对齐算子实例。
    /// </summary>
    /// <param name="serviceProvider">用于解析服务的依赖注入容器。</param>
    /// <param name="logger">记录对齐过程日志的记录器。</param>
    /// <param name="strategyFactory">按模型名创建强制对齐策略的工厂。</param>
    /// <param name="toolManagerFactory">用于创建 CrispASR 工具管理器的工厂。</param>
    public AlignmentOperator(
        IServiceProvider serviceProvider,
        ILogger<AlignmentOperator> logger,
        IAlignmentStrategyFactory strategyFactory,
        IToolManagerFactory toolManagerFactory)
        : base(logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _toolManagerFactory = toolManagerFactory ?? throw new ArgumentNullException(nameof(toolManagerFactory));
    }

    /// <summary>
    /// 执行强制对齐：读取当前句子与转换后的音频，经策略细化词级时间戳，
    /// 并将结果写回 <see cref="SubtitleWorkflowContext"/> 状态。对齐失败时保留原始时间。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供句子、音频路径与配置。</param>
    /// <param name="cancellationToken">用于取消对齐过程的取消标记。</param>
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
        var audioPath = context.State.PreprocessedAudioPath ?? context.State.ConvertedAudioPath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            throw new InvalidOperationException($"Audio file not found or not converted: {audioPath}");
        }

        // Report progress.
        OnProgress(0, "Preparing alignment...");

        // 5. Get model name（未指定时默认 Qwen3 Forced-Aligner，跨语言支持中英文；
        //    默认名必须是模型注册表中真实存在的键，否则 ModelManager 会抛
        //    "Unsupported model" 导致对齐失败并保留原时间）
        var modelName = context.Config.AlignmentModel ?? "qwen3-forced-aligner-0.6b";

        // 6. Create strategy via factory
        if (modelName != null)
        {
            var strategy = _strategyFactory.Create(modelName);

            var metadata = CorrectionMetadata.Get(context.State);
            var originalTimings = sentences.ToDictionary(
                sentence => sentence,
                sentence => new SentenceTiming(
                    sentence.Start,
                    sentence.End,
                    sentence.Words.Select(word => new WordTiming(word.Start, word.End)).ToList()));

            // 7. Execute alignment (sentences keep their original text)
            LogInfo($"Using alignment model: {modelName}");
            OnProgress(30, "Running alignment...");

            List<Sentence> alignedSentences;
            try
            {
                alignedSentences = await strategy.AlignAsync(sentences, audioPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Forced alignment failed; keeping all original subtitle timings.");
                foreach (var sentence in sentences)
                {
                    sentence.Start = originalTimings[sentence].Start;
                    sentence.End = originalTimings[sentence].End;
                }
                alignedSentences = sentences;
            }

            if (alignedSentences.Count != sentences.Count)
            {
                var actualCount = alignedSentences?.Count ?? 0;
                var message = $"Alignment changed the sentence count from {sentences.Count} to {actualCount}.";
                _logger.LogError(message);
                context.State.Errors.Add(message);
                throw new AlignmentException(message);
            }

            // 8. Update context
            var alignedBySource = new Dictionary<Sentence, Sentence>();
            for (var index = 0; index < sentences.Count; index++)
            {
                var original = sentences[index];
                var aligned = alignedSentences[index];
                alignedBySource[original] = aligned;
                if (!ReferenceEquals(original, aligned) && metadata.TryGetValue(original, out var originalMetadata))
                    metadata[aligned] = originalMetadata;
                var timing = originalTimings[original];
                if (!HasTimingChange(aligned, timing))
                {
                    MarkKept(context, metadata, aligned, timing, unmatched: true);
                    continue;
                }

                var driftMs = aligned.Start - timing.Start;
                SetMetadata(metadata, aligned, CorrectKeys.DriftMs, driftMs);
                SetMetadata(metadata, aligned, CorrectKeys.Action,
                    Math.Abs(driftMs) > context.Config.MaxDriftMs ? "retimed" : "kept");
                if (Math.Abs(driftMs) > context.Config.MaxDriftMs)
                    context.State.Report.TimelineShifted++;
            }
            context.State.AlignedSentences =
            [
                .. sentences
                    .Select(sentence => alignedBySource.GetValueOrDefault(sentence, sentence))
            ];
            context.State.CurrentSentences = context.State.AlignedSentences;
            context.State.CorrectedSentences = context.State.AlignedSentences;
            context.State.IsAligned = true;

            OnProgress(100, "Alignment completed");
            LogInfo($"Alignment finished. Processed {alignedSentences.Count} sentences.");
        }
    }

    private static bool HasTimingChange(Sentence sentence, SentenceTiming original)
    {
        if (sentence.Start != original.Start || sentence.End != original.End)
            return true;

        return sentence.Words.Count != original.Words.Count ||
               sentence.Words.Where((word, index) =>
                   word.Start != original.Words[index].Start || word.End != original.Words[index].End).Any();
    }

    private static void MarkKept(
        SubtitleWorkflowContext context,
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        SentenceTiming original,
        bool unmatched)
    {
        sentence.Start = original.Start;
        sentence.End = original.End;
        for (var index = 0; index < Math.Min(sentence.Words.Count, original.Words.Count); index++)
        {
            sentence.Words[index].Start = original.Words[index].Start;
            sentence.Words[index].End = original.Words[index].End;
        }

        SetMetadata(metadata, sentence, CorrectKeys.DriftMs, 0d);
        SetMetadata(metadata, sentence, CorrectKeys.Action, "kept");
        if (unmatched)
            context.State.Report.Unmatched++;
    }

    private static void SetMetadata(
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        string key,
        object value)
    {
        if (!metadata.TryGetValue(sentence, out var values))
            metadata[sentence] = values = new Dictionary<string, object>();
        values[key] = value;
    }

    private sealed record SentenceTiming(double Start, double End, List<WordTiming> Words);
    private sealed record WordTiming(double Start, double End);

    /// <summary>
    /// 检查对齐运行环境：确认 CrispASR 可执行文件是否就绪，缺失时记录警告。
    /// </summary>
    /// <param name="cancellationToken">用于取消健康检查的取消标记。</param>
    public override async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        LogInfo("Checking alignment environment...");

        var toolManager = _toolManagerFactory.Create("crispasr");
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
