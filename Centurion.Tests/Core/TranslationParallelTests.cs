using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.Translation;
using Centurion.Models;
using Microsoft.Extensions.AI;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>LLM 翻译批并行：并发确实发生、结果与串行一致。</summary>
public sealed class TranslationParallelTests
{
    private sealed class FakeChatClient(
        Func<IList<ChatMessage>, CancellationToken, Task<ChatResponse>> handler) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => handler(messages.ToList(), cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ChatClientMetadata Metadata => new("fake");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static List<Sentence> MakeSentences(int count)
        => Enumerable.Range(0, count)
            .Select(i => new Sentence { Text = $"Line {i}.", Start = i * 1000, End = (i + 1) * 1000 })
            .ToList();

    /// <summary>记录并发峰值；每批按批内索引返回译文（与批大小解耦）。</summary>
    private static (FakeChatClient Client, ConcurrentDictionary<int, int> BatchIds, PeakGauge Gauge) BuildBatchClient()
    {
        var gauge = new PeakGauge();
        var batchIds = new ConcurrentDictionary<int, int>();
        var id = 0;
        FakeChatClient client = new(async (messages, ct) =>
        {
            gauge.Enter();
            try
            {
                // 异步让出，使并发批次在时间上真实重叠
                await Task.Delay(15, ct);
                var current = Interlocked.Increment(ref id) - 1;
                batchIds[current] = 1;
                var prompt = messages.LastOrDefault()?.Text ?? string.Empty;
                // 译文按句子内容生成（与调用顺序无关），保证并行/串行结果可比
                var texts = ExtractSentenceTexts(prompt);
                var items = texts
                    .Select((text, i) => new LLMTranslationStrategy.TranslationItem { Id = i, Translation = $"T:{text}" })
                    .ToList();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonToText(items)));
            }
            finally
            {
                gauge.Exit();
            }
        });
        return (client, batchIds, gauge);
    }

    private static List<string> ExtractSentenceTexts(string prompt)
    {
        // prompt 尾部是 JSON payload：[{"id":0,"text":"..."},...]
        var matches = Regex.Matches(prompt, "\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"");
        return matches.Select(m => m.Groups[1].Value).ToList();
    }

    private static string JsonToText(IEnumerable<LLMTranslationStrategy.TranslationItem> items)
        => "[" + string.Join(",", items.Select(i => $"{{\"id\":{i.Id},\"translation\":\"{i.Translation}\"}}")) + "]";

    private sealed class PeakGauge
    {
        private int _active;
        private int _peak;
        public void Enter()
        {
            var now = Interlocked.Increment(ref _active);
            var peak = Volatile.Read(ref _peak);
            while (now > peak && Interlocked.CompareExchange(ref _peak, now, peak) != peak)
                peak = Volatile.Read(ref _peak);
        }

        public void Exit() => Interlocked.Decrement(ref _active);
        public int Peak => Volatile.Read(ref _peak);
    }

    [Fact]
    public async Task TranslateAsync_WithConcurrency_ExecutesBatchesInParallel()
    {
        var (client, batchIds, gauge) = BuildBatchClient();
        var strategy = new LLMTranslationStrategy(client);
        var sentences = MakeSentences(10);

        await strategy.TranslateAsync(sentences, new TranslationOptions
        {
            TargetLanguage = "zh",
            BatchSize = 2,
            MaxConcurrency = 3
        });

        // 5 个批全部发起；并发峰值 >1 证明并行实际发生
        Assert.Equal(5, batchIds.Count);
        Assert.True(gauge.Peak > 1, $"Expected parallel batches, peak concurrency was {gauge.Peak}.");
    }

    [Fact]
    public async Task TranslateAsync_ConcurrencyOne_BehavesSequentially()
    {
        var (client, batchIds, gauge) = BuildBatchClient();
        var strategy = new LLMTranslationStrategy(client);
        var sentences = MakeSentences(6);

        await strategy.TranslateAsync(sentences, new TranslationOptions
        {
            TargetLanguage = "zh",
            BatchSize = 2,
            MaxConcurrency = 1
        });

        Assert.Equal(3, batchIds.Count);
        Assert.Equal(1, gauge.Peak);
    }

    [Fact]
    public async Task TranslateAsync_ParallelAndSequential_ProduceIdenticalResults()
    {
        var (clientParallel, _, _) = BuildBatchClient();
        var (clientSequential, _, _) = BuildBatchClient();
        var parallel = new LLMTranslationStrategy(clientParallel);
        var sequential = new LLMTranslationStrategy(clientSequential);

        var parallelSentences = MakeSentences(8);
        var sequentialSentences = MakeSentences(8);

        await parallel.TranslateAsync(parallelSentences, new TranslationOptions
        {
            TargetLanguage = "zh", BatchSize = 2, MaxConcurrency = 4
        });
        await sequential.TranslateAsync(sequentialSentences, new TranslationOptions
        {
            TargetLanguage = "zh", BatchSize = 2, MaxConcurrency = 1
        });

        Assert.Equal(
            sequentialSentences.Select(s => s.TranslatedText),
            parallelSentences.Select(s => s.TranslatedText));
    }
}
