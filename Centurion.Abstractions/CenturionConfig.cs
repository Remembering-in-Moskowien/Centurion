using System.Text.Json;

namespace Centurion.Abstractions;

/// <summary>
/// <c>centurion.config.json</c> 配置 + 环境变量覆盖。
/// 加载优先级：内置默认 &lt; <c>centurion.config.json</c> &lt; <c>CENTURION_*</c> 环境变量 &lt; 命令行参数。
/// </summary>
/// <example>
/// 配置文件示例（当前目录 centurion.config.json）：
/// <code>
/// { "profile": "offline", "language": "zh-CN", "outputFormat": "ass" }
/// </code>
/// 环境变量：CENTURION_PROFILE、CENTURION_LANG、CENTURION_GITHUB_PROXY、
/// CENTURION_LOG_LEVEL、CENTURION_JSON、CENTURION_OUTPUT_FORMAT。
/// </example>
public sealed class CenturionConfig
{
    /// <summary>Provider 选型 profile：offline / fast / quality / cheap。</summary>
    public string? Profile { get; set; }

    /// <summary>运行时 UI 语言代码（如 zh-CN）。</summary>
    public string? Language { get; set; }

    /// <summary>GitHub 下载加速代理前缀（留空/空串表示直连）。</summary>
    public string? GithubProxy { get; set; }

    /// <summary>默认以 JSON 输出结果摘要。</summary>
    public bool? Json { get; set; }

    /// <summary>日志级别：trace / debug / information / warning / error / critical。</summary>
    public string? LogLevel { get; set; }

    /// <summary>默认字幕输出格式：ass / srt / txt。</summary>
    public string? OutputFormat { get; set; }

    /// <summary>init 向导生成的推荐命令链（辅助展示，不参与解析）。</summary>
    public string? Recipe { get; set; }

    /// <summary>从当前目录 centurion.config.json + 环境变量加载配置。</summary>
    public static CenturionConfig Load(string? configPath = null)
    {
        var config = new CenturionConfig();

        var path = configPath ?? FindConfigFile();
        if (path is not null && File.Exists(path))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<CenturionConfig>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is not null)
                    config = parsed;
            }
            catch (JsonException)
            {
                // 配置文件非法：静默降级为默认（CLI 启动时由调用方决定是否提示）
            }
        }

        // 环境变量覆盖配置文件
        if (GetEnv("CENTURION_PROFILE") is { } p && p.Length > 0) config.Profile = p;
        if (GetEnv("CENTURION_LANG") is { } l && l.Length > 0) config.Language = l;
        if (GetEnv("CENTURION_GITHUB_PROXY") is { } g) config.GithubProxy = g.Length == 0 ? "" : g;
        if (GetEnv("CENTURION_LOG_LEVEL") is { } ll && ll.Length > 0) config.LogLevel = ll;
        if (GetEnv("CENTURION_JSON") is { } j && bool.TryParse(j, out var bj)) config.Json = bj;
        if (GetEnv("CENTURION_OUTPUT_FORMAT") is { } of && of.Length > 0) config.OutputFormat = of;

        return config;
    }

    /// <summary>按搜索路径查找配置文件（./centurion.config.json，向上两级）。</summary>
    private static string? FindConfigFile()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 3 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "centurion.config.json");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string? GetEnv(string name)
    {
        try { return Environment.GetEnvironmentVariable(name); }
        catch (System.Security.SecurityException) { return null; }
    }
}
