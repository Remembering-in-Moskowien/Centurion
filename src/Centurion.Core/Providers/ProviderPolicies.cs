using System.Collections.Concurrent;
using Centurion.Abstractions.Providers;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// Provider 横切执行策略：重试（指数退避）、限流（滑动窗口）、熔断（连续失败冷却）。
/// 每个 Provider 名一个独立状态，线程安全；由 fallback 链在每次调用前/后驱动。
/// </summary>
public sealed class ProviderPolicies(ProviderPolicyOptions options)
{
    private readonly ProviderPolicyOptions _options = options;

    // 熔断状态：provider 名 → (连续失败次数, 熔断截止时间)
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset OpenUntil)> _breakers = new();

    // 限流滑动窗口：provider 名 → 调用时间戳队列
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _rateWindows = new();

    /// <summary>当前配置。</summary>
    public ProviderPolicyOptions Options => _options;

    /// <summary>本运行累计估算花费（USD），跨链共享（同一 policies 实例多次 ExecuteAsync 累计）。</summary>
    public double BudgetSpentUsd { get; private set; }

    /// <summary>记录一次成功调用产生的估算成本。</summary>
    public void RecordSpend(double costUsd)
    {
        if (costUsd > 0)
            BudgetSpentUsd += costUsd;
    }

    /// <summary>检查是否允许发起调用：未熔断且未超限流。</summary>
    public bool CanInvoke(string providerName)
    {
        if (_options.CircuitBreakerFailureThreshold > 0
            && _breakers.TryGetValue(providerName, out var state)
            && state.OpenUntil > DateTimeOffset.UtcNow)
            return false;

        if (_options.MaxCallsPerMinute > 0 && !AcquireRateSlot(providerName))
            return false;

        return true;
    }

    /// <summary>调用成功后记录：清零熔断计数（如已冷却则恢复）。</summary>
    public void OnSuccess(string providerName)
    {
        if (_breakers.TryGetValue(providerName, out var state) && state.OpenUntil <= DateTimeOffset.UtcNow)
            _breakers.TryRemove(providerName, out _);
    }

    /// <summary>调用失败后记录：达到阈值即熔断。</summary>
    public void OnFailure(string providerName)
    {
        if (_options.CircuitBreakerFailureThreshold <= 0)
            return;

        var state = _breakers.AddOrUpdate(
            providerName,
            (1, DateTimeOffset.MinValue),
            (_, existing) => (existing.Failures + 1, existing.OpenUntil));

        if (state.Failures >= _options.CircuitBreakerFailureThreshold)
        {
            _breakers[providerName] = (state.Failures, DateTimeOffset.UtcNow.AddSeconds(_options.CircuitBreakerCooldownSeconds));
        }
    }

    /// <summary>按重试配置执行委托（指数退避），耗尽抛 ProviderExecutionException。</summary>
    public async Task<T> WithRetryAsync<T>(
        string providerName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       && ex is not ProviderUnavailableException
                                       && attempt < _options.MaxRetries)
            {
                attempt++;
                var delay = _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
            }
            catch (ProviderUnavailableException)
            {
                throw; // 不可用不重试，直接交链切换
            }
        }
    }

    private bool AcquireRateSlot(string providerName)
    {
        var now = DateTimeOffset.UtcNow;
        var window = _rateWindows.GetOrAdd(providerName, _ => new Queue<DateTimeOffset>());
        lock (window)
        {
            while (window.Count > 0 && now - window.Peek() > TimeSpan.FromMinutes(1))
                window.Dequeue();

            if (window.Count >= _options.MaxCallsPerMinute)
                return false;

            window.Enqueue(now);
            return true;
        }
    }
}
