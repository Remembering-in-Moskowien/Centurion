using Centurion.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// 术语表加载器：从外部 JSON 文件读取翻译术语映射（{源语言术语: 目标语言术语}）。
/// 支持字典对象（{ "term": "译文" }）与数组对象（[{ "source": "...", "target": "..." }]）两种形态。
/// </summary>
public static class GlossaryLoader
{
    /// <summary>字典形态术语表条目。</summary>
    private sealed class GlossaryEntry
    {
        /// <summary>源语言术语。</summary>
        public string? Source { get; set; }

        /// <summary>目标语言术语。</summary>
        public string? Target { get; set; }
    }

    /// <summary>
    /// 从 JSON 文件加载术语表；文件缺失或格式非法时返回空表并记录警告。
    /// </summary>
    /// <param name="path">术语表 JSON 文件路径；为空或不存在时返回空表。</param>
    /// <param name="logger">可选的日志器，用于记录加载告警。</param>
    /// <returns>术语映射字典（大小写不敏感键）。</returns>
    public static Dictionary<string, string> Load(string? path, ILogger? logger = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path))
            return result;

        if (!File.Exists(path))
        {
            logger?.LogWarning("Glossary file not found: {Path}", path);
            return result;
        }

        try
        {
            var json = File.ReadAllText(path).Trim();
            if (json.StartsWith('['))
            {
                var entries = JsonParser.Deserialize<List<GlossaryEntry>>(json);
                if (entries is not null)
                {
                    foreach (var entry in entries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Target))
                            result[entry.Source] = entry.Target;
                    }
                }
            }
            else
            {
                var dict = JsonParser.Deserialize<Dictionary<string, string>>(json);
                if (dict is not null)
                {
                    foreach (var pair in dict)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                            result[pair.Key] = pair.Value;
                    }
                }
            }

            logger?.LogInformation("Loaded {Count} glossary term(s) from {Path}.", result.Count, path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load glossary '{Path}': {Message}", path, ex.Message);
        }

        return result;
    }
}
