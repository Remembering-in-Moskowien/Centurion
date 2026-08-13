using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Centurion.Core;

/// <summary>
/// Provides utility methods for ASS subtitle parsing and format conversion.
/// </summary>
internal static class SubTools
{
    /// <summary>
    /// Converts an ASS subtitle time string to milliseconds.
    /// </summary>
    /// <param name="time">The ASS subtitle time string in the format HH:MM:SS.CC.</param>
    /// <returns>The time value in milliseconds.</returns>
    /// <exception cref="FormatException">Thrown when the time string is invalid.</exception>
    public static long TimeToLong(string time)
    {
        var parts = time.Split(':', '.');
        if (parts.Length != 4) throw new FormatException("Invalid time format.");

        var hours = int.Parse(parts[0]);
        var minutes = int.Parse(parts[1]);
        var seconds = int.Parse(parts[2]);
        var centiseconds = int.Parse(parts[3]);

        long totalMilliseconds = hours * 3600000 + minutes * 60000 + seconds * 1000 + centiseconds * 10;
        return totalMilliseconds;
    }

    /// <summary>
    /// Converts a time value in milliseconds to an ASS subtitle time string.
    /// </summary>
    /// <param name="time">The time value in milliseconds.</param>
    /// <returns>The ASS subtitle time string.</returns>
    public static string LongToTime(long time)
    {
        var totalMilliseconds = time;
        var hours = (int)(totalMilliseconds / 3600000);
        totalMilliseconds %= 3600000;
        var minutes = (int)(totalMilliseconds / 60000);
        totalMilliseconds %= 60000;
        var seconds = (int)(totalMilliseconds / 1000);
        var centiseconds = (int)(totalMilliseconds % 1000 / 10);

        return $"{hours:D2}:{minutes:D2}:{seconds:D2}.{centiseconds:D2}";
    }

    /// <summary>
    /// Extracts a matched text value from the given regular expression.
    /// </summary>
    /// <param name="reg">The regular expression used to match content.</param>
    /// <param name="content">The text content to search.</param>
    /// <param name="defaultValue">The default value of an unsuccessful match</param>
    /// <returns>The first captured group if a match is found; otherwise, the default string.</returns>
    public static string GetText(Regex reg, string content, string defaultValue = "")
    {
        if (string.IsNullOrWhiteSpace(content))
            return defaultValue;

        var match = reg.Match(content);
        if (!match.Success)
            return defaultValue;

        var trimmed = match.Groups[1].Value.Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? defaultValue : trimmed;
    }

    public static HashVerifyResult VerifyHash(string path, string hash)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var actualHash = Convert.ToHexStringLower(sha256.ComputeHash(stream));
        return new HashVerifyResult
        {
            IsMatch = string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase),
            ActualHash = actualHash
        };
    }

    public sealed class HashVerifyResult
    {
        public bool IsMatch { get; set; }
        public string ActualHash { get; set; } = string.Empty;
    }

    private static readonly Regex ConvertRegex =
        new(@"(?i)^\s*([0-9]+(?:\.[0-9]+)?)\s*([KMGT]?i?B|[KMGT]|B)?\s*$", RegexOptions.Compiled);

    public static double ParseSizeToBytes(string size)
    {
        if (string.IsNullOrWhiteSpace(size)) return 0d;

        size = size.Trim();
        var m = ConvertRegex.Match(size);
        if (!m.Success) return 0d;

        var num = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Value.ToUpperInvariant();
        var multiplier = unit switch
        {
            "KIB" => 1024d,
            "MIB" => 1024d * 1024d,
            "GIB" => 1024d * 1024d * 1024d,
            "TIB" => 1024d * 1024d * 1024d * 1024d,
            "KB" => 1000d,
            "MB" => 1000d * 1000d,
            "GB" => 1000d * 1000d * 1000d,
            "TB" => 1000d * 1000d * 1000d * 1000d,
            "K" => 1024d,
            "M" => 1024d * 1024d,
            "G" => 1024d * 1024d * 1024d,
            "T" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };
        return num * multiplier;
    }
}