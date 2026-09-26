using System.Text.RegularExpressions;
using Centurion.Models.Console;
using Centurion.Core.Infrastructure;
using Centurion.Models.Workflow;
using Centurion.Models.Ass;

namespace Centurion.Models.Ass;

/// <summary>
/// ASS完整字幕文档构建器，组装脚本信息、样式、对话行
/// </summary>
public partial class AssSubBuilder : BuilderBase<AssSubBuilder, AssSub>
{
    private string _title = string.Empty;
    private string _scriptType = string.Empty;
    private string _wrapStyle = string.Empty;
    private string _collisions = string.Empty;
    private string _playResX = string.Empty;
    private string _playResY = string.Empty;
    private float _timer;
    private List<AssStyle> _styles = [];
    private List<AssSubLine> _lines = [];

    /// <summary>字幕标题，写入 Script Info 的 Title 字段。</summary>
    public string Title => _title;
    /// <summary>脚本版本标识（如 v4.00+）。</summary>
    public string ScriptType => _scriptType;
    /// <summary>自动换行规则（WrapStyle 编号）。</summary>
    public string WrapStyle => _wrapStyle;
    /// <summary>字幕时间重叠时的处理策略（Normal 或 Reverse）。</summary>
    public string Collisions => _collisions;
    /// <summary>基准播放分辨率宽度。</summary>
    public string PlayResX => _playResX;
    /// <summary>基准播放分辨率高度。</summary>
    public string PlayResY => _playResY;
    /// <summary>时间轴缩放系数（百分比，100 为正常速）。</summary>
    public float Timer => _timer;
    /// <summary>文档中定义的样式集合。</summary>
    public List<AssStyle> Styles => _styles;
    /// <summary>对话/注释字幕行集合。</summary>
    public List<AssSubLine> Lines => _lines;

    /// <summary>设置字幕标题</summary>
    public AssSubBuilder WithTitle(string value)
    {
        return Set(ref _title, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置脚本版本</summary>
    public AssSubBuilder WithScriptType(string value)
    {
        return Set(ref _scriptType, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置自动换行规则</summary>
    public AssSubBuilder WithWrapStyle(string value)
    {
        return Set(ref _wrapStyle, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置字幕重叠处理策略</summary>
    public AssSubBuilder WithCollisions(string value)
    {
        return Set(ref _collisions, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置基准分辨率宽度</summary>
    public AssSubBuilder WithPlayResX(string value)
    {
        return Set(ref _playResX, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置基准分辨率高度</summary>
    public AssSubBuilder WithPlayResY(string value)
    {
        return Set(ref _playResY, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置时间缩放系数</summary>
    public AssSubBuilder WithTimer(float value)
    {
        return Set(ref _timer, value);
    }

    /// <summary>批量设置样式集合</summary>
    public AssSubBuilder WithStyles(List<AssStyle> value)
    {
        return Set(ref _styles, value ?? []);
    }

    /// <summary>批量设置字幕行集合</summary>
    public AssSubBuilder WithLines(List<AssSubLine> value)
    {
        return Set(ref _lines, value ?? []);
    }

    /// <summary>是否自动添加Default默认样式</summary>
    public AssSubBuilder WithAddDefaultStyle()
    {
        return WithStyles([new AssStyleBuilder().WithDefaultValues().Build()]);
    }

    /// <summary>填充一套标准ASS默认脚本配置（含主字幕 Default 与次字幕 Sub 两套样式）。</summary>
    public AssSubBuilder WithDefaultValues()
    {
        return WithTitle("Default AssSub file")
            .WithScriptType("v4.00+")
            .WithWrapStyle("0")
            .WithCollisions("Normal")
            .WithPlayResX("1920")
            .WithPlayResY("1080")
            .WithTimer(100.0f)
            .WithStyles(
            [
                new AssStyleBuilder().WithDefaultValues().Build(),
                new AssStyleBuilder().WithSubtitleStyle().Build()
            ])
            .WithLines([]);
    }

    /// <summary>从完整ASS文本解析生成文档构建器</summary>
    /// <param name="content">完整ASS文件字符串</param>
    /// <returns>填充完成的构建器</returns>
    /// <exception cref="FormatException">文件结构非法</exception>
    public static AssSubBuilder FromContent(string content)
    {
        var builder = new AssSubBuilder();
        var scriptInfoMatch = ScriptInfoBlockRegex().Match(content);
        if (scriptInfoMatch.Success)
        {
            var scriptInfoContent = scriptInfoMatch.Groups[1].Value;
            builder = builder
                .WithTitle(SubTools.GetText(TitleRegex(), scriptInfoContent, "默认字幕"))
                .WithScriptType(SubTools.GetText(ScriptTypeRegex(), scriptInfoContent, "v4.00+"))
                .WithWrapStyle(SubTools.GetText(WrapStyleRegex(), scriptInfoContent, "0"))
                .WithCollisions(SubTools.GetText(CollisionsRegex(), scriptInfoContent, "Normal"))
                .WithPlayResX(SubTools.GetText(PlayResXRegex(), scriptInfoContent, "1920"))
                .WithPlayResY(SubTools.GetText(PlayResYRegex(), scriptInfoContent, "1080"));
            var timerStr = SubTools.GetText(TimerRegex(), scriptInfoContent);
            if (float.TryParse(timerStr, out var t))
                builder = builder.WithTimer(t);
        }
        else
        {
            builder = builder.WithDefaultValues();
        }

        // 解析所有样式
        var styles = new List<AssStyle>();
        var stylesMatch = V4StyleBlockRegex().Match(content);
        if (stylesMatch.Success)
        {
            var stylesContent = stylesMatch.Groups[1].Value;
            foreach (var line in stylesContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("Style:"))
                    styles.Add(AssStyleBuilder.FromContent(line).Build());

            styles = [.. styles.Distinct()];
        }

        // 解析出样式时保留原样；无任何样式时才补默认 Default
        builder = styles.Count > 0 ? builder.WithStyles(styles) : builder.WithAddDefaultStyle();

        // 解析所有对话行并按起始时间排序
        var lines = new List<AssSubLine>();
        var eventsMatch = EventsBlockRegex().Match(content);
        if (eventsMatch.Success)
        {
            var eventsContent = eventsMatch.Groups[1].Value;
            foreach (var line in eventsContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("Dialogue:") || line.StartsWith("Comment:"))
                    lines.Add(AssSubLineBuilder.FromContent(line).Build());

            lines = [.. lines.OrderBy(l => l.GetStart())];
        }

        return builder.WithLines(lines);
    }

    /// <summary>直接读取ASS文件并解析为构建器</summary>
    public static AssSubBuilder FromFile(string path)
    {
        return FromContent(File.ReadAllText(path));
    }

    /// <summary>
    /// Creates an ASS subtitle builder from a workflow context.
    /// Uses aligned/diarized sentences with word-level timings to generate dialogue lines.
    /// If KaraokeMode is enabled, each word is wrapped with \k tags (centiseconds).
    /// </summary>
    /// <param name="context">The workflow context containing sentence data and configuration.</param>
    /// <param name="output">控制台输出端口（可选；缺省时回退到全局门面）</param>
    /// <returns>A builder pre-populated with default script info and subtitle lines.</returns>
    public static AssSubBuilder FromWorkflow(SubtitleWorkflowContext context, IConsoleOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        output ??= ConsoleServices.Output;
        var builder = new AssSubBuilder().WithDefaultValues();
        if (!string.IsNullOrEmpty(context.Config.InputFilePath))
            builder = builder.WithTitle(Path.GetFileNameWithoutExtension(context.Config.InputFilePath));

        // 样式表：优先使用工作流状态中的自定义样式（Studio 前端经中间文件编辑）；
        // 为空时回退到内置默认两套（Default + Sub）。始终保证 Default 存在，
        // 双语布局时保证 Sub 次字幕样式存在。
        var customStyles = context.State.Styles;
        List<AssStyle> styles;
        if (customStyles is { Count: > 0 })
        {
            styles = [.. customStyles];
            if (styles.All(s => !string.Equals(s.Name, "Default", StringComparison.OrdinalIgnoreCase)))
                styles.Add(new AssStyleBuilder().WithDefaultValues().Build());
        }
        else
        {
            styles =
            [
                new AssStyleBuilder().WithDefaultValues().Build(),
                new AssStyleBuilder().WithSubtitleStyle().Build()
            ];
        }

        if (context.Config.Bilingual && styles.All(s => !string.Equals(s.Name, "Sub", StringComparison.OrdinalIgnoreCase)))
            styles.Add(new AssStyleBuilder().WithSubtitleStyle().Build());

        builder = builder.WithStyles(styles);

        var sentences = context.State.CorrectedSentences is { Count: > 0 }
            ? context.State.CorrectedSentences
            : context.State.CurrentSentences is { Count: > 0 }
                ? context.State.CurrentSentences
                : context.State.AlignedSentences is { Count: > 0 }
                    ? context.State.AlignedSentences
                    : context.State.CoarseSentences;
        if (sentences is null or { Count: 0 })
            return builder.WithLines([]);

        if (context.State.ScriptSentences.Count > 1 && sentences.Count == 1)
        {
            var warning = $"The workflow has {context.State.ScriptSentences.Count} script lines but only one sentence reached subtitle generation; check the alignment operator for an unintended merge.";
            context.State.Warnings.Add(warning);
            output.WriteWarning(warning);
        }

        var lines = new List<AssSubLine>();
        foreach (var sentence in sentences)
        {
            if (sentence.SkipRender || sentence.End < sentence.Start)
                continue;
            // 零时长句（End == Start，如对齐输出瞬间词）不再整句丢弃；
            // 补最小 80ms 时长渲染，避免句末词丢失
            if (sentence.End <= sentence.Start)
                sentence.End = sentence.Start + 80;

            // 说话人：仅当分割已成功完成时才视为有效（未分割时词级为占位标签，不写入）
            var speaker = context.State.IsDiarized ? sentence.Speaker : null;
            var showLabels = context.Config.ShowSpeakerLabels;

            var words = sentence.Words ?? [];
            var useRawSentence = words.Count == 0 || words.All(word => word.Status == MappingStatus.ScriptMissing);
            var tokens = useRawSentence
                ? [new DisplayToken(sentence.Text, [])]
                : BuildDisplayTokens(sentence, context.Config.FillGapWithEllipsis);
            if (useRawSentence)
            {
                output.WriteWarning($"Sentence has no words, using raw text: {sentence.Text}");
            }

            if (tokens.Count == 0)
                tokens = [new DisplayToken(sentence.Text, [])];

            // 翻译输出：TranslatedText 非空时优先显示译文。
            // 单语：一行（主字幕 Default 样式）；双语：两行——主行原文（Default，上方 MarginV 100）、
            // 次行译文（Sub 样式，贴底 MarginV 28），参照 Theme.ass 的 eng/chi 主次布局。
            // KaraokeMode 下翻译句不再跳过：译文用时间插值 + 长音节词多分配构建词级 \K 时间戳。
            // 说话人标签加在主行（原文/唯一行）文本前；ASS Name 字段在所有行写入。
            if (!string.IsNullOrWhiteSpace(sentence.TranslatedText))
            {
                var targetLanguage = string.IsNullOrWhiteSpace(context.Config.TargetLanguage)
                    ? "zh"
                    : context.Config.TargetLanguage;

                if (context.Config.Bilingual)
                {
                    var mainText = context.Config.KaraokeMode
                        ? string.Join(" ", tokens.Select(FormatKaraokeToken))
                        : Centurion.Models.Text.LanguageSupport.JoinMixed(
                            tokens.Select(token => token.Text));
                    lines.Add(BuildLine(sentence, mainText, "Default", speaker, showLabels));

                    var subText = context.Config.KaraokeMode
                        ? TranslationKaraokeBuilder.Build(
                            sentence.TranslatedText, sentence.Start, sentence.End, targetLanguage)
                        : sentence.TranslatedText;
                    lines.Add(BuildLine(sentence, subText, "Sub", speaker, false, useSentenceStyle: false));
                }
                else
                {
                    var translated = context.Config.KaraokeMode
                        ? TranslationKaraokeBuilder.Build(
                            sentence.TranslatedText, sentence.Start, sentence.End, targetLanguage)
                        : sentence.TranslatedText;
                    lines.Add(BuildLine(sentence, translated, "Default", speaker, showLabels));
                }

                continue;
            }

            var dialogue = context.Config.KaraokeMode
                ? string.Join(" ", tokens.Select(FormatKaraokeToken))
                : Centurion.Models.Text.LanguageSupport.JoinMixed(
                    tokens.Select(token => token.Text));
            lines.Add(BuildLine(sentence, dialogue, "Default", speaker, showLabels));
        }

        return builder.WithLines([.. lines.OrderBy(line => line.GetStart())]);
    }

    /// <summary>
    /// 构建对话行。style 为回退样式名；<paramref name="useSentenceStyle"/> 为 true 时
    /// 优先使用句子绑定的 <see cref="Sentence.Style"/>（Studio 逐行指定）。
    /// </summary>
    private static AssSubLine BuildLine(Sentence sentence, string text, string style, string? speaker, bool showSpeakerPrefix, bool useSentenceStyle = true)
    {
        var finalStyle = useSentenceStyle && !string.IsNullOrWhiteSpace(sentence.Style)
            ? sentence.Style.Trim()
            : style;

        var finalText = showSpeakerPrefix && !string.IsNullOrWhiteSpace(speaker)
            ? $"[{speaker}] {text}"
            : text;

        return new AssSubLineBuilder().WithComment(false)
            .WithLayer(0)
            .WithStart((long)sentence.Start)
            .WithEnd((long)sentence.End)
            .WithStyle(finalStyle)
            .WithName(speaker ?? string.Empty)
            .WithMarginL(0)
            .WithMarginR(0)
            .WithMarginV(0)
            .WithEffect(string.Empty)
            .WithText(finalText)
            .Build();
    }

    private sealed record DisplayToken(string Text, IReadOnlyList<Word> Words);

    private static List<DisplayToken> BuildDisplayTokens(Sentence sentence, bool fillGapWithEllipsis)
    {
        var result = new List<DisplayToken>();
        var words = sentence.Words ?? [];
        var spontPrefixAdded = false;
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            if (word.Status == MappingStatus.ScriptMissing)
            {
                // ScriptMissing 词是真实存在的词（转录/台本未匹配），必须保留文本显示，
                // 否则句末未匹配词会被静默丢弃造成"末尾丢词"。
                // fillGapWithEllipsis 仅用于无词内容的间隙占位，这里不再跳过词本身。
                result.Add(new DisplayToken(word.Text, [word]));
                continue;
            }

            if (word.Status != MappingStatus.AudioExtra)
            {
                result.Add(new DisplayToken(word.Text, [word]));
                continue;
            }

            var extraTexts = new List<string>();
            var extraWords = new List<Word>();
            while (index < words.Count && words[index].Status == MappingStatus.AudioExtra)
            {
                extraWords.Add(words[index]);
                var extraText = words[index].Text;
                if (extraText.StartsWith("[SPONT]", StringComparison.OrdinalIgnoreCase))
                    extraText = extraText["[SPONT]".Length..].TrimStart();
                if (!string.IsNullOrWhiteSpace(extraText))
                    extraTexts.Add(extraText);
                index++;
            }

            index--;
            if (extraTexts.Count > 0)
            {
                var prefix = spontPrefixAdded ? string.Empty : "[SPONT] ";
                result.Add(new DisplayToken(prefix + string.Join(" ", extraTexts), extraWords));
                spontPrefixAdded = true;
            }
        }

        return result;
    }

    private static string FormatKaraokeToken(DisplayToken token)
    {
        if (token.Words.Count == 0 || token.Words.All(word => word.Status == MappingStatus.ScriptMissing))
            return token.Text;
        var prefix = token.Text.StartsWith("[SPONT] ", StringComparison.Ordinal) ? "[SPONT] " : string.Empty;
        var words = token.Words.Select(word =>
        {
            var durationMs = Math.Max(0, word.End - word.Start);
            return $"{{\\K{(int)(durationMs / 10)}}}{word.Text}";
        });
        return prefix + string.Join(" ", words);
    }

    /// <summary>组装所有配置，生成完整AssSub字幕文档</summary>
    public override AssSub Build()
    {
        return new AssSub(
            _title,
            _scriptType,
            _wrapStyle,
            _collisions,
            _playResX,
            _playResY,
            _timer,
            [.. _styles],
            [.. _lines]
        );
    }

    // 脚本信息块正则
    [GeneratedRegex(@"Title:\s*(.*)", RegexOptions.None)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"ScriptType:\s*(.*)", RegexOptions.None)]
    private static partial Regex ScriptTypeRegex();

    [GeneratedRegex(@"WrapStyle:\s*(.*)", RegexOptions.None)]
    private static partial Regex WrapStyleRegex();

    [GeneratedRegex(@"Collisions:\s*(.*)", RegexOptions.None)]
    private static partial Regex CollisionsRegex();

    [GeneratedRegex(@"PlayResX:\s*(.*)", RegexOptions.None)]
    private static partial Regex PlayResXRegex();

    [GeneratedRegex(@"PlayResY:\s*(.*)", RegexOptions.None)]
    private static partial Regex PlayResYRegex();

    [GeneratedRegex(@"Timer:\s*(.*)", RegexOptions.None)]
    private static partial Regex TimerRegex();

    [GeneratedRegex(@"\[Script Info\](.*?)\[V4\+ Styles\]", RegexOptions.Singleline)]
    private static partial Regex ScriptInfoBlockRegex();

    [GeneratedRegex(@"\[V4\+ Styles\](.*?)\[Events\]", RegexOptions.Singleline)]
    private static partial Regex V4StyleBlockRegex();

    [GeneratedRegex(@"\[Events\](.*)", RegexOptions.Singleline)]
    private static partial Regex EventsBlockRegex();
}
