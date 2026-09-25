using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Tts;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// TTS 合成算子（dub Phase 3）：为每句调用 TTS 引擎合成 wav 片段。
/// 支持并行合成（按说话人分桶，桶内保序、桶间并行，受 <c>DubConfig.TtsParallelism</c> 全局限流）
/// 与长句分块（目标时长超过阈值时按比例拆分子段，连续放置后由混音阶段拼接）。
/// 单句合成失败记录 Warning 并标记 Skipped，不阻断整条管道。
/// 结果写入 State.Extensions["DubSegments"]（List&lt;DubSegment&gt;，按原句序）。
/// </summary>
public sealed class TtsSynthesisOperator(
    ITtsEngine ttsEngine,
    ILogger<TtsSynthesisOperator> logger)
    : PipelineOperatorBase<TtsSynthesisOperator>(logger)
{
    /// <summary>单句最大字符数：超过则截断（避免 TTS 超长句失败）。</summary>
    private const int MaxCharsPerSentence = 200;

    /// <summary>算子名称。</summary>
    public override string Name => "TTS Synthesis";

    /// <summary>
    /// 合成全部句子：先按目标时长/字符数把长句拆成多个工作项（分块），
    /// 再按说话人分组并行合成（同一说话人串行保序），最后按原始句序重组 DubSegments。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0)
        {
            LogWarning("No sentences to synthesize.");
            return;
        }

        var references = context.State.Extensions.TryGetValue("DubSpeakerReferences", out var rawRefs)
            ? rawRefs as Dictionary<string, string> ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();

        var parallelism = Math.Max(1, context.Config.TtsParallelism);
        var maxChunkSeconds = Math.Max(5, context.Config.DubMaxChunkSeconds);
        var tempDir = context.State.PipelineTempDirectory ?? throw new InvalidOperationException("Pipeline temp directory is not initialized.");

        // 1) 构造工作项（长句分块）：保留原始顺序索引
        var workItems = new List<WorkItem>();
        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var text = (sentence.TranslatedText ?? sentence.Text).Trim();
            if (text.Length == 0)
                continue;

            var targetMs = Math.Max(0, sentence.End - sentence.Start);
            var chunks = SplitLongSentence(text, targetMs, maxChunkSeconds);
            var chunkDuration = targetMs / (double)chunks.Count;
            for (var c = 0; c < chunks.Count; c++)
            {
                workItems.Add(new WorkItem
                {
                    SentenceIndex = i,
                    Text = chunks[c],
                    Speaker = sentence.Speaker,
                    Reference = sentence.Speaker != null && references.TryGetValue(sentence.Speaker, out var r) ? r : null,
                    TargetStartMs = sentence.Start + c * chunkDuration,
                    TargetEndMs = c == chunks.Count - 1 ? sentence.End : sentence.Start + (c + 1) * chunkDuration
                });
            }
        }

        // 2) 按说话人分桶（null 说话人合并为一桶），桶内保序
        var buckets = workItems
            .GroupBy(w => w.Speaker ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(w => w.SentenceIndex).ThenBy(w => w.TargetStartMs).ToList())
            .ToList();

        var segments = new List<DubSegment>();
        using var gate = new SemaphoreSlim(parallelism, parallelism);
        var lockObject = new object();

        // 3) 桶间并行合成；同桶串行（按序）
        var bucketTasks = buckets.Select(async bucket =>
        {
            foreach (var item in bucket)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = new DubSegment
                {
                    Text = item.Text,
                    SpeakerId = item.Speaker,
                    ReferenceAudioPath = item.Reference,
                    TargetStartMs = item.TargetStartMs,
                    TargetEndMs = item.TargetEndMs,
                    SynthesizedWavPath = Path.Combine(tempDir, $"dub_{item.SentenceIndex:D4}_{item.TargetStartMs:000000}.wav")
                };

                await gate.WaitAsync(cancellationToken);
                try
                {
                    segment.SynthesizedDurationSec = await ttsEngine.SynthesizeAsync(
                        item.Text, item.Reference, context.Config.TtsLanguage, segment.SynthesizedWavPath!, cancellationToken);
                }
                catch (TtsSynthesisException ex)
                {
                    segment.Skipped = true;
                    segment.Note = ex.Message;
                    LogWarning($"TTS failed for sentence {item.SentenceIndex + 1}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    segment.Skipped = true;
                    segment.Note = ex.Message;
                    LogWarning($"TTS error for sentence {item.SentenceIndex + 1}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                }

                lock (lockObject)
                {
                    segments.Add(segment);
                }
            }
        });

        await Task.WhenAll(bucketTasks);

        // 4) 按原始顺序（句子索引升序，同一句内子段按时序）重组
        var finalSegments = segments
            .OrderBy(s => GetSentenceOrder(s.TargetStartMs, sentences))
            .ThenBy(s => s.TargetStartMs)
            .ToList();

        context.State.Extensions["DubSegments"] = finalSegments;
        var skipped = finalSegments.Count(s => s.Skipped);
        OnProgress(100, $"Synthesized {finalSegments.Count - skipped}/{finalSegments.Count} segments");
        LogInfo($"TTS synthesis completed: {finalSegments.Count} segments ({buckets.Count} speaker groups, parallelism {parallelism}), {skipped} skipped.");
    }

    private static int GetSentenceOrder(double targetStartMs, List<Sentence> sentences)
    {
        for (var i = 0; i < sentences.Count; i++)
        {
            if (Math.Abs(sentences[i].Start - targetStartMs) < 1)
                return i;
        }
        return sentences.Count;
    }

    /// <summary>
    /// 长句分块：目标时长超过 maxChunkSeconds 时按时长比例把文本切成若干子句。
    /// 中文/日文按字符切，其他按空格/标点切；子句数下限 1、上限 8。
    /// </summary>
    /// <param name="text">目标文本。</param>
    /// <param name="targetMs">目标时长（毫秒）。</param>
    /// <param name="maxChunkSeconds">单块最大目标时长（秒）。</param>
    /// <returns>子句列表（至少一项）。</returns>
    internal static List<string> SplitLongSentence(string text, double targetMs, double maxChunkSeconds)
    {
        if (targetMs <= 0 || targetMs / 1000.0 <= maxChunkSeconds || text.Length <= 2)
            return [text];

        var chunkCount = Math.Clamp((int)Math.Ceiling(targetMs / 1000.0 / maxChunkSeconds), 2, 8);
        if (text.Length <= chunkCount)
            return [text];

        return SplitText(text, chunkCount);
    }

    /// <summary>
    /// 把文本按字符数尽量均匀地切成 n 块，优先在空白/标点处断开（英文），CJK 直接按字符切。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="chunkCount">目标块数。</param>
    /// <returns>切分后的子句列表。</returns>
    internal static List<string> SplitText(string text, int chunkCount)
    {
        var isCjk = text.Any(ch => ch > 0x2E80);
        var results = new List<string>();

        if (!isCjk)
        {
            // 英文等空格语系：优先在标点/空格断开
            var tokens = text.Split(' ');
            var targetPerChunk = Math.Ceiling(tokens.Length / (double)chunkCount);
            var current = new List<string>();
            foreach (var token in tokens)
            {
                current.Add(token);
                if (current.Count >= targetPerChunk)
                {
                    results.Add(string.Join(' ', current).Trim());
                    current.Clear();
                }
            }
            if (current.Count > 0)
                results.Add(string.Join(' ', current).Trim());
            if (results.Count == 1 && results[0].Length == 0)
                results = [text];
            return results;
        }

        // CJK：按字符均匀切
        var perChunk = Math.Max(1, (int)Math.Ceiling(text.Length / (double)chunkCount));
        for (var i = 0; i < text.Length; i += perChunk)
            results.Add(text.Substring(i, Math.Min(perChunk, text.Length - i)));
        return results;
    }

    /// <summary>内部工作项：句子索引 + 目标文本 + 说话人/参考 + 目标时间窗。</summary>
    private sealed class WorkItem
    {
        public int SentenceIndex { get; init; }
        public string Text { get; init; } = string.Empty;
        public string? Speaker { get; init; }
        public string? Reference { get; init; }
        public double TargetStartMs { get; init; }
        public double TargetEndMs { get; init; }
    }
}
