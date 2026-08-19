using System.Text.RegularExpressions;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;

namespace Centurion.Core.Parsers;

/// <summary>
/// SRT 字幕解析器
/// </summary>
public class SrtParser : ISubtitleParser
{
    public string SupportedExtension => ".srt";

    private static readonly Regex TimeStampReg =
        new(@"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})", RegexOptions.Compiled);

    private static readonly Regex IndexReg = new(@"^\d+$", RegexOptions.Compiled);

    public async Task<List<SubtitleEntry>> ParseAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"SRT file not found: {filePath}");

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var entries = new List<SubtitleEntry>();

        int i = 0;
        while (i < lines.Length)
        {
            ct.ThrowIfCancellationRequested();

            // 跳过空行
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                i++;
                continue;
            }

            // 序号行
            if (IndexReg.IsMatch(lines[i]))
            {
                i++;
                // 时间行
                if (i < lines.Length && TimeStampReg.IsMatch(lines[i]))
                {
                    var match = TimeStampReg.Match(lines[i]);
                    var startMs = (long)new TimeSpan(
                        0,
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value)
                    ).TotalMilliseconds;

                    var endMs = (long)new TimeSpan(
                        0,
                        int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value),
                        int.Parse(match.Groups[7].Value),
                        int.Parse(match.Groups[8].Value)
                    ).TotalMilliseconds;

                    i++;
                    var textLines = new List<string>();
                    while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        textLines.Add(lines[i]);
                        i++;
                    }

                    var text = string.Join(@"\N", textLines);
                    entries.Add(new SubtitleEntry
                    {
                        StartMs = startMs,
                        EndMs = endMs,
                        Text = text
                    });
                }
            }
            else
            {
                i++;
            }
        }

        return entries;
    }
}