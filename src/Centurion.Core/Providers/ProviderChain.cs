using Centurion.Abstractions.Providers;
using Microsoft.Extensions.Logging;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// fallback 链执行器：按序尝试主 Provider → 备用 Provider。
/// - 不可用（无密钥/服务离线）自动跳过；
/// - 执行失败（重试耗尽）切换到备用；
/// - 预算上限内聚合用量（token/音频秒/缓存命中/估算成本）；
/// - 全部失败抛 <see cref="ProviderExecutionException"/>。
/// </summary>
public static class ProviderChain
{
    /// <summary>
    /// 沿链执行委托。
    /// </summary>
    /// <typeparam name="T">结果值类型。</typeparam>
    /// <param name="chain">按优先级排列的 Provider 链（首个为主）。</param>
    /// <param name="invoke">执行单个 Provider 的委托。</param>
    /// <param name="policies">横切策略（重试/熔断/限流）。</param>
    /// <param name="budgetUsdPerRun">本次运行预算上限（0 = 不限）；超出后跳过后续云端调用。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>首个成功结果；用量聚合全部已执行 Provider。</returns>
    /// <exception cref="ProviderExecutionException">链全部不可用或全部失败时抛出。</exception>
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

            // 预算闸门：云 Provider 且预算已用尽 → 跳过（本地不花钱）
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
