using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 转换管道 - 使用 <see cref="SubtitleFileParser"/> 解析输入字幕文件，
/// 并将每个字幕条目转换为 Sentence 对象存入 TranscribeSentences。
/// ASS 输入使用项目自有解析器，保留样式表与逐行样式。
/// </summary>
public sealed class ConvertParseOperator : IPipelineOperator
{
    /// <summary>算子在管道中的显示名称。</summary>
    public string Name => "ConvertParse";

    /// <summary>
    /// 解析输入字幕文件并将每个字幕条目转换为 <see cref="Centurion.Models.Sentence"/>，写入工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供字幕文件路径。</param>
    /// <param name="cancellationToken">用于取消解析过程的取消标记。</param>
    public Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var inputPath = context.Config.SubtitleFilePath ?? context.Config.InputFilePath;
        var parsed = SubtitleFileParser.Parse(inputPath!, context.Config.Language);

        var sentences = parsed.Sentences;

        // ASS 样式表写入状态（供 build 渲染时复用原始样式）
        if (parsed.Styles.Count > 0)
            context.State.Styles = [.. parsed.Styles];

        // 保留独立基线；转换管道仍使用 TranscribeSentences 作为现有输出槽。
        context.State.SubtitleSentences = sentences.Select(CloneSentence).ToList();
        context.State.TranscribeSentences = sentences;
        context.State.CurrentSentences = sentences;
        return Task.CompletedTask;
    }

    private static Centurion.Models.Sentence CloneSentence(Centurion.Models.Sentence source)
    {
        return new Centurion.Models.Sentence
        {
            Text = source.Text,
            CleanedText = source.CleanedText,
            Start = source.Start,
            End = source.End,
            SkipRender = source.SkipRender,
            Style = source.Style,
            Words = source.Words.Select(word => new Centurion.Models.Word
            {
                Text = word.Text,
                Start = word.Start,
                End = word.End,
                Speaker = word.Speaker,
                PosTag = word.PosTag,
                Status = word.Status
            }).ToList()
        };
    }
}
