using Centurion.Models.Providers;
namespace Centurion.Core.Providers;

/// <summary>
/// 统一 API 密钥读取：环境变量（CENTURION_&lt;域&gt;_API_KEY）优先，
/// 其次显式配置值，最后本地默认。缺失返回 null（云端 Provider 不可用，链自动回退本地）。
/// </summary>
public static class ApiKeyStore
{
    /// <summary>按域读取密钥：环境变量 → 显式配置。</summary>
    /// <param name="domain">域标识（asr/ocr/llm），用于构造环境变量名。</param>
    /// <param name="configured">显式配置的密钥；为空时回退环境变量。</param>
    /// <returns>密钥；未配置时返回 null。</returns>
    public static string? Resolve(string domain, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var envKey = $"CENTURION_{domain.ToUpperInvariant()}_API_KEY";
        var fromEnv = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    /// <summary>按域读取端点：环境变量 CENTURION_&lt;域&gt;_BASE_URL → 显式配置 → null。</summary>
    public static string? ResolveBaseUrl(string domain, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var envKey = $"CENTURION_{domain.ToUpperInvariant()}_BASE_URL";
        var fromEnv = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }
}
