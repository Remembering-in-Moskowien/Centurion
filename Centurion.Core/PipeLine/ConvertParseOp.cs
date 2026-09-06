// File: Centurion.Core/Operators/ConvertParseOp.cs
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using SubtitlesParserV2;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 转换管道 - 使用 SubtitlesParserV2 解析输入字幕文件，
/// 并将每个字幕条目转换为 Sentence 对象存入 TranscribeSentences。
/// </summary>
public class ConvertParseOp : IPipelineOperator
{
    public string Name => "ConvertParse";

    public async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var inputPath = context.Config.InputFilePath;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException("Subtitle file not found.", inputPath);

        await using var stream = File.OpenRead(inputPath);
        var subtitle = SubtitleParser.ParseStream(stream)?.Subtitles;

        if (subtitle == null || subtitle.Count == 0)
            throw new InvalidOperationException("No subtitle items parsed.");

        // 转换为 Sentence 列表
        var sentences = subtitle
            .Where(item => item.Lines.Count > 0)
            .Select(item => new Sentence
            {
                Text = string.Join(" ", item.Lines),      // 多行合并为一行，空格分隔
                Start = item.StartTime,                  // 毫秒 (int)
                End = item.EndTime,
                Words = [] // 转换场景无词级信息
            })
            .ToList();

        // 存入 TranscribeSentences（与转录结果同构）
        context.State.TranscribeSentences = sentences;
    }
}