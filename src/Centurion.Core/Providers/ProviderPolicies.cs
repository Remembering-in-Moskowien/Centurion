using System.Collections.Concurrent;
using Centurion.Abstractions.Providers;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// Cross-cutting provider execution policies: retry (exponential backoff), rate limiting
/// (sliding window), and circuit breaking (cooldown after consecutive failures). One independent
/// state per provider name, thread-safe; driven by the fallback chain around each call.
/// </summary>
public sealed class ProviderPolicies(ProviderPolicyOptions options)
{
    private readonly ProviderPolicyOptions _options = options;

    // Circuit-breaker state: provider name → (consecutive failure count, circuit-open deadline)
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset OpenUntil)> _breakers = new();

    // Rate-limit sliding window: provider name → queue of call timestamps
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _rateWindows = new();

    /// <summary>The current configuration.</summary>
    public ProviderPolicyOptions Options => _options;

    /// <summary>Cumulative estimated spend (USD) for this run, shared across chains (accumulated across multiple ExecuteAsync calls on the same policies instance).</summary>
    public double BudgetSpentUsd { get; private set; }

    /// <summary>Records the estimated cost produced by one successful call.</summary>
    public void RecordSpend(double costUsd)
    {
        if (costUsd > 0)
            BudgetSpentUsd += costUsd;
    }

    /// <summary>Checks whether a call may be made: not open on the breaker and within the rate limit.</summary>
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

    /// <summary>Records a successful call: resets the breaker count (recovering after cooldown).</summary>
    public void OnSuccess(string providerName)
    {
        if (_breakers.TryGetValue(providerName, out var state) && state.OpenUntil <= DateTimeOffset.UtcNow)
            _breakers.TryRemove(providerName, out _);
    }

    /// <summary>Records a failed call: trips the breaker once the threshold is reached.</summary>
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

    /// <summary>Runs the delegate per the retry policy (exponential backoff); throws ProviderExecutionException when retries are exhausted.</summary>
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
                throw; // Unavailable providers are not retried; the chain switches over immediately.
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
