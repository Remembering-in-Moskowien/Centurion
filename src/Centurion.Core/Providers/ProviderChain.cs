using Centurion.Abstractions.Providers;
using Microsoft.Extensions.Logging;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// Fallback-chain executor: tries providers in order from primary to backup.
/// - Unavailable providers (no key / service offline) are skipped automatically;
/// - On execution failure (retries exhausted), it switches to the backup;
/// - Aggregates usage (tokens / audio seconds / cache hits / estimated cost) within the budget cap;
/// - Throws <see cref="ProviderExecutionException"/> when all providers fail.
/// </summary>
public static class ProviderChain
{
    /// <summary>
    /// Runs the delegate along the chain.
    /// </summary>
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="chain">The provider chain ordered by priority (first is primary).</param>
    /// <param name="invoke">The delegate that executes a single provider.</param>
    /// <param name="policies">Cross-cutting policies (retry / circuit breaker / rate limit).</param>
    /// <param name="budgetUsdPerRun">Budget cap for this run (0 = unlimited); once exceeded, subsequent cloud calls are skipped.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The first successful result; usage aggregates across all providers that ran.</returns>
    /// <exception cref="ProviderExecutionException">Thrown when the whole chain is unavailable or all providers fail.</exception>
    public static async Task<ProviderResult<T>> ExecuteAsync<T>(
        IReadOnlyList<IProvider> chain,
        Func<IProvider, CancellationToken, Task<ProviderResult<T>>> invoke,
        ProviderPolicies policies,
        double budgetUsdPerRun,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (chain.Count == 0)
            throw new ProviderExecutionException("Provider chain is empty.", "(none)");

        var totalUsage = new List<ProviderUsage>();
        var failures = new List<string>();
        var attempted = false;

        foreach (var provider in chain)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!policies.CanInvoke(provider.Name))
            {
                logger.LogDebug("Provider {Provider} skipped (rate limit or circuit open).", provider.Name);
                failures.Add(provider.Name);
                continue;
            }

            // Budget gate: cloud providers are skipped once the budget is exhausted (local calls cost nothing)
            if (provider.Capabilities.Kind == ProviderKind.Cloud
                && budgetUsdPerRun > 0
                && policies.BudgetSpentUsd >= budgetUsdPerRun)
            {
                logger.LogInformation("Budget limit reached (${Spent:F4}); skipping cloud provider {Provider}.",
                    policies.BudgetSpentUsd, provider.Name);
                failures.Add(provider.Name);
                continue;
            }

            if (!await provider.IsAvailableAsync(cancellationToken))
            {
                logger.LogDebug("Provider {Provider} unavailable (no key / service offline); falling through.", provider.Name);
                failures.Add(provider.Name);
                continue;
            }

            attempted = true;
            try
            {
                var result = await policies.WithRetryAsync(
                    provider.Name,
                    ct => invoke(provider, ct),
                    cancellationToken);

                policies.OnSuccess(provider.Name);
                policies.RecordSpend(result.Usage.EstimatedCostUsd);
                totalUsage.Add(result.Usage);
                if (budgetUsdPerRun > 0 && policies.BudgetSpentUsd > budgetUsdPerRun)
                    logger.LogWarning("Provider budget exceeded (${Spent:F4} > ${Budget:F4}).",
                        policies.BudgetSpentUsd, budgetUsdPerRun);
                return new ProviderResult<T>(result.Value, Aggregate(totalUsage));
            }
            catch (ProviderUnavailableException ex)
            {
                policies.OnFailure(provider.Name);
                logger.LogWarning("Provider {Provider} unavailable during execution: {Message}", provider.Name, ex.Message);
                failures.Add(provider.Name);
            }
            catch (ProviderExecutionException ex)
            {
                policies.OnFailure(provider.Name);
                logger.LogWarning("Provider {Provider} failed: {Message}", provider.Name, ex.Message);
                failures.Add(provider.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                policies.OnFailure(provider.Name);
                logger.LogWarning("Provider {Provider} threw: {Message}", provider.Name, ex.Message);
                failures.Add(provider.Name);
            }
        }

        if (!attempted)
            throw new ProviderUnavailableException(
                $"No provider in the chain is available: {string.Join(", ", failures)}",
                failures.FirstOrDefault() ?? "(none)");

        throw new ProviderExecutionException(
            $"All providers failed: {string.Join(", ", failures)}", failures.Last() ?? "(none)");
    }

    private static ProviderUsage Aggregate(List<ProviderUsage> usages)
    {
        if (usages.Count == 0)
            return new ProviderUsage { ProviderName = "(none)" };

        var total = usages[0];
        foreach (var usage in usages.Skip(1))
            total += usage;
        return total;
    }
}
