using Centurion.Abstractions.Pipeline;
using Centurion.Core.Utils.Media;
using Centurion.Core.Utils.Parsing;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 合并管道的来源解析算子：把 combine 命令的全部输入（媒体容器 / 裸字幕文件）
/// 统一解析为进程内的来源列表（<see cref="SubtitleSourceItem"/>，写入 State.Extensions），
/// 每个来源携带自己的句子集合与 ASS 样式表。
/// 媒体容器经 <see cref="MediaSubtitleExtractor"/> 枚举/提取内封字幕轨（按 --tracks 过滤），
/// 裸字幕文件直接解析。单轨提取失败仅告警跳过；零来源时抛异常终止。
/// </summary>
public sealed class CombineParseOperator(
    MediaSubtitleExtractor extractor,
    ILogger<CombineParseOperator> logger)
    : PipelineOperatorBase<CombineParseOperator>(logger)
{
    /// <summary>State.Extensions 中来源列表的键（进程内，merge 算子消费）。</summary>
    public const string SourcesKey = "CombineSources";

    /// <summary>State.Extensions 中媒体输入路径列表的键（命令装配时写入）。</summary>
    public const string MediaInputsKey = "CombineMediaInputs";

    /// <summary>State.Extensions 中裸字幕文件列表的键（命令装配时写入）。</summary>
    public const string SubtitleInputsKey = "CombineSubtitleInputs";

    /// <summary>State.Extensions 中轨道号数组的键（null = 全部字幕轨）。</summary>
    public const string TrackFilterKey = "CombineTrackFilter";

    /// <summary>算子名称。</summary>
    public override string Name => "Combine Parse";

    /// <summary>
    /// 解析全部输入为来源列表并写入工作流状态扩展槽。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">所有输入均未解析出任何字幕来源。</exception>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var mediaInputs = GetExtensionsValue<string[]>(context, MediaInputsKey) ?? [];
        var subtitleInputs = GetExtensionsValue<string[]>(context, SubtitleInputsKey) ?? [];
        var trackFilter = GetExtensionsValue<int[]?>(context, TrackFilterKey);
        var outputDir = context.State.PipelineTempDirectory
            ?? throw new InvalidOperationException("combine pipeline requires a temp directory for extracted tracks.");

        var sources = new List<SubtitleSourceItem>();

        foreach (var media in mediaInputs)
        {
            var tracks = await extractor.ListSubtitleTracksAsync(media, cancellationToken);
            if (tracks.Count == 0)
            {
                var warning = ConsoleServices.T("No extractable subtitle tracks in {0}.", media);
                ConsoleServices.Output.WriteWarning(warning);
                LogWarning($"No subtitle tracks in '{media}'.");
                continue;
            }

            if (trackFilter is { Length: > 0 })
                tracks = tracks.Where(t => trackFilter.Contains(t.TrackId)).ToList();

            if (tracks.Count == 0)
            {
                var warning = ConsoleServices.T("No subtitle tracks matched --tracks in {0}.", media);
                ConsoleServices.Output.WriteWarning(warning);
                LogWarning($"--tracks filtered all tracks out of '{media}'.");
                continue;
            }

            foreach (var track in tracks)
            {
                var extracted = await extractor.ExtractTrackAsync(media, track, outputDir, cancellationToken);
                if (extracted is null)
                {
                    var warning = ConsoleServices.T("Subtitle track {0} of {1} could not be extracted; skipped.", track.TrackId, media);
                    ConsoleServices.Output.WriteWarning(warning);
                    LogWarning(warning);
                    continue;
                }

                var name = BuildMediaSourceName(media, track);
                var parsed = ParseQuietly(extracted, name, context.Config.Language);
                if (parsed is not null)
                    sources.Add(parsed);
            }
        }

        foreach (var file in subtitleInputs)
        {
            var name = Path.GetFileName(file);
            var parsed = ParseQuietly(file, name, context.Config.Language);
            if (parsed is not null)
                sources.Add(new SubtitleSourceItem
                {
                    Name = name,
                    Sentences = parsed.Sentences,
                    Styles = parsed.Styles
                });
        }

        if (sources.Count == 0)
            throw new InvalidOperationException("No subtitle sources parsed from the given inputs.");

        context.State.Extensions[SourcesKey] = sources;
        var summary = string.Join("; ", sources.Select(s => $"{s.Name}: {s.Sentences.Count} lines"));
        LogInfo($"Parsed {sources.Count} subtitle source(s): {summary}");
    }

    /// <summary>解析单个字幕文件为来源；解析失败仅告警返回 null（单轨损坏不阻断合并）。</summary>
    private SubtitleSourceItem? ParseQuietly(string path, string displayName, string? language)
    {
        try
        {
            var parsed = SubtitleFileParser.Parse(path, language);
            return new SubtitleSourceItem
            {
                Name = displayName,
                Sentences = parsed.Sentences,
                Styles = parsed.Styles
            };
        }
        catch (Exception ex)
        {
            var warning = ConsoleServices.T("Subtitle source {0} could not be parsed; skipped ({1}).", displayName, ex.Message);
            ConsoleServices.Output.WriteWarning(warning);
            LogWarning(warning);
            return null;
        }
    }

    /// <summary>来源显示名：媒体文件名 + 轨道号 +（语言/名称）。</summary>
    private static string BuildMediaSourceName(string mediaPath, MkvTrackInfo track)
    {
        var qualifier = !string.IsNullOrWhiteSpace(track.Name) ? track.Name
            : !string.IsNullOrWhiteSpace(track.Language) && !track.Language.Equals("und", StringComparison.OrdinalIgnoreCase)
                ? track.Language
                : null;
        var suffix = qualifier is null ? "" : $" ({qualifier})";
        return $"{Path.GetFileName(mediaPath)} #track {track.TrackId}{suffix}";
    }

    /// <summary>从 Extensions 读取强类型值（类型不符时返回 null 并告警）。</summary>
    private static T? GetExtensionsValue<T>(SubtitleWorkflowContext context, string key)
    {
        if (!context.State.Extensions.TryGetValue(key, out var raw))
            return default;
        if (raw is T typed)
            return typed;
        return default;
    }
}
