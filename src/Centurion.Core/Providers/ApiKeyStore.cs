using Centurion.Models.Providers;
namespace Centurion.Core.Providers;

/// <summary>
/// Unified API-key resolution: the environment variable (CENTURION_&lt;domain&gt;_API_KEY) takes
/// precedence, followed by the explicit configured value, then the local default. Returns null
/// when missing (cloud providers become unavailable and the chain automatically falls back to local).
/// </summary>
public static class ApiKeyStore
{
    /// <summary>Resolves the key for a domain: environment variable → explicit configuration.</summary>
    /// <param name="domain">Domain identifier (asr/ocr/llm), used to build the environment variable name.</param>
    /// <param name="configured">The explicitly configured key; falls back to the environment variable when empty.</param>
    /// <returns>The key; returns null when not configured.</returns>
    public static string? Resolve(string domain, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var envKey = $"CENTURION_{domain.ToUpperInvariant()}_API_KEY";
        var fromEnv = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    /// <summary>Resolves the endpoint for a domain: environment variable CENTURION_&lt;domain&gt;_BASE_URL → explicit configuration → null.</summary>
    public static string? ResolveBaseUrl(string domain, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var envKey = $"CENTURION_{domain.ToUpperInvariant()}_BASE_URL";
        var fromEnv = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }
}
