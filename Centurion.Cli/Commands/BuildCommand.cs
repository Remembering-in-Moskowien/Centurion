using Centurion.Cli.Commands.Settings;
using Centurion.Core.Utils;
using Centurion.Models.Ass;
using Centurion.Core.Infrastructure;
using Centurion.Abstractions.Utils;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>build</c> 命令：把 Centurion 中间文件（*.centurion.json）渲染为字幕文件。
/// 支持三种输出格式：ASS（默认，含样式/双语/卡拉OK/说话人）、SRT（纯文本时间轴）、TXT（纯文本行）。
/// 格式由 --format 指定或从 -o 扩展名推断；中间文件由 spawn/from-script/correct/translate/dub/convert 生成。
/// </summary>
public sealed class BuildCommand(ILogger<BuildCommand> logger) : AsyncCommand<BuildSettings>
{
    /// <summary>
    /// 执行构建：加载中间文件 → 按格式渲染 → 写出字幕文件。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">build 命令选项。</param>
    /// <param name="ct">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, BuildSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);

            if (!CenturionFileIO.IsCenturionFile(inputPath))
                throw new InvalidDataException(
                    $"'{inputPath}' is not a Centurion intermediate file. Convert subtitles first: 'Centurion convert <file>'.");

            // 格式解析：--format 优先，其次 -o 扩展名，默认 ass
            var format = ResolveFormat(settings);
            var outputPath = settings.OutputFile?.FullName ?? DefaultOutputPath(inputPath, format);

            var workflowContext = await CenturionFileIO.LoadAsync(inputPath, ct);

            var content = format switch
            {
                BuildFormat.Srt => SubtitleFormatRenderer.RenderSrt(workflowContext),
                BuildFormat.Txt => SubtitleFormatRenderer.RenderTxt(workflowContext),
                _ => AssSubBuilder.FromWorkflow(workflowContext).Build().ToString()
            };

            await File.WriteAllTextAsync(outputPath, content, ct);

            var sentenceCount = workflowContext.State.CurrentSentences.Count;
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle built: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Rendered {0} sentences as {1} -> {2}",
                sentenceCount, format.ToString().ToLowerInvariant(), outputPath));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Build pipeline execution failed.");
            return 1;
        }
    }

    /// <summary>按 --format / -o 扩展名解析目标格式。</summary>
    private static BuildFormat ResolveFormat(BuildSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Format))
        {
            return settings.Format.ToLowerInvariant() switch
            {
                "srt" => BuildFormat.Srt,
                "txt" => BuildFormat.Txt,
                "ass" or "ssa" => BuildFormat.Ass,
                _ => throw new ArgumentException(
                    $"Unsupported build format '{settings.Format}'. Use ass, srt or txt.")
            };
        }

        if (settings.OutputFile is not null)
        {
            return Path.GetExtension(settings.OutputFile.FullName).ToLowerInvariant() switch
            {
                ".srt" => BuildFormat.Srt,
                ".txt" => BuildFormat.Txt,
                ".ass" or ".ssa" => BuildFormat.Ass,
                "" => BuildFormat.Ass,
                var unknown => throw new ArgumentException(
                    $"Unsupported output extension '{unknown}'. Use .ass, .srt or .txt (or set --format).")
            };
        }

        return BuildFormat.Ass;
    }

    /// <summary>按格式计算默认输出路径：中间文件名去掉 .centurion.json 后加对应扩展名。</summary>
    private static string DefaultOutputPath(string inputPath, BuildFormat format)
    {
        var baseName = CenturionFileIO.DefaultOutputPath(Path.GetFileName(inputPath))
            .Replace(CenturionFileIO.Extension, "", StringComparison.OrdinalIgnoreCase);
        var extension = format switch
        {
            BuildFormat.Srt => ".srt",
            BuildFormat.Txt => ".txt",
            _ => ".ass"
        };
        var dir = Path.GetDirectoryName(inputPath);
        return string.IsNullOrEmpty(dir)
            ? baseName + extension
            : Path.Combine(dir, baseName + extension);
    }
}

/// <summary>build 命令支持的输出格式。</summary>
public enum BuildFormat
{
    /// <summary>ASS 字幕（默认，样式/双语/卡拉OK/说话人全支持）。</summary>
    Ass,
    /// <summary>SRT 字幕（纯文本时间轴）。</summary>
    Srt,
    /// <summary>纯文本（每句一行）。</summary>
    Txt
}
