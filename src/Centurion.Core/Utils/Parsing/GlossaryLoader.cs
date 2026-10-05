using Centurion.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Glossary loader: reads translation term mappings from an external JSON file
/// ({source-language term: target-language term}).
/// Supports both the dictionary object form ({ "term": "translation" }) and the
/// array-of-objects form ([{ "source": "...", "target": "..." }]).
/// </summary>
public static class GlossaryLoader
{
    /// <summary>A glossary entry in the dictionary form.</summary>
    private sealed class GlossaryEntry
    {
        /// <summary>The source-language term.</summary>
        public string? Source { get; set; }

        /// <summary>The target-language term.</summary>
        public string? Target { get; set; }
    }

    /// <summary>
    /// Load the glossary from a JSON file; returns an empty map and logs a warning when the file is missing or malformed.
    /// </summary>
    /// <param name="path">Path to the glossary JSON file; returns an empty map when null or non-existent.</param>
    /// <param name="logger">Optional logger for recording load warnings.</param>
    /// <returns>The term mapping dictionary (case-insensitive keys).</returns>
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
