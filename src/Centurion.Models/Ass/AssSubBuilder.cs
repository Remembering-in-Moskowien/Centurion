using System.Text.RegularExpressions;
using Centurion.Models.Workflow;
using Centurion.Models.Ass;
using Centurion.Models.Console;
namespace Centurion.Models.Ass;

/// <summary>
/// Builder for complete ASS subtitle documents, assembling script information, styles, and dialogue lines.
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

    /// <summary>Subtitle title written to the Script Info Title field.</summary>
    public string Title => _title;
    /// <summary>Script version identifier, such as v4.00+.</summary>
    public string ScriptType => _scriptType;
    /// <summary>Automatic line-wrapping mode (WrapStyle value).</summary>
    public string WrapStyle => _wrapStyle;
    /// <summary>Handling policy for overlapping subtitles (Normal or Reverse).</summary>
    public string Collisions => _collisions;
    /// <summary>Reference playback resolution width.</summary>
    public string PlayResX => _playResX;
    /// <summary>Reference playback resolution height.</summary>
    public string PlayResY => _playResY;
    /// <summary>Timeline scale percentage; 100 means normal speed.</summary>
    public float Timer => _timer;
    /// <summary>Styles defined in the document.</summary>
    public List<AssStyle> Styles => _styles;
    /// <summary>Dialogue and comment subtitle lines.</summary>
    public List<AssSubLine> Lines => _lines;

    /// <summary>Sets the subtitle title.</summary>
    public AssSubBuilder WithTitle(string value)
    {
        return Set(ref _title, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the script version.</summary>
    public AssSubBuilder WithScriptType(string value)
    {
        return Set(ref _scriptType, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the automatic line-wrapping mode.</summary>
    public AssSubBuilder WithWrapStyle(string value)
    {
        return Set(ref _wrapStyle, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the subtitle overlap handling policy.</summary>
    public AssSubBuilder WithCollisions(string value)
    {
        return Set(ref _collisions, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the reference resolution width.</summary>
    public AssSubBuilder WithPlayResX(string value)
    {
        return Set(ref _playResX, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the reference resolution height.</summary>
    public AssSubBuilder WithPlayResY(string value)
    {
        return Set(ref _playResY, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the time scale.</summary>
    public AssSubBuilder WithTimer(float value)
    {
        return Set(ref _timer, value);
    }

    /// <summary>Sets the style collection.</summary>
    public AssSubBuilder WithStyles(List<AssStyle> value)
    {
        return Set(ref _styles, value ?? []);
    }

    /// <summary>Sets the subtitle line collection.</summary>
    public AssSubBuilder WithLines(List<AssSubLine> value)
    {
        return Set(ref _lines, value ?? []);
    }

    /// <summary>Adds the default style.</summary>
    public AssSubBuilder WithAddDefaultStyle()
    {
        return WithStyles([new AssStyleBuilder().WithDefaultValues().Build()]);
    }

    /// <summary>Populates standard ASS defaults, including primary Default and secondary Sub styles.</summary>
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

    /// <summary>Parses complete ASS text into a document builder.</summary>
    /// <param name="content">Complete ASS file text.</param>
    /// <returns>A populated builder.</returns>
    /// <exception cref="FormatException">Thrown when the file structure is invalid.</exception>
    public static AssSubBuilder FromContent(string content)
    {
        var builder = new AssSubBuilder();
        var scriptInfoMatch = ScriptInfoBlockRegex().Match(content);
        if (scriptInfoMatch.Success)
        {
            var scriptInfoContent = scriptInfoMatch.Groups[1].Value;
            builder = builder
                .WithTitle(SubTools.GetText(TitleRegex(), scriptInfoContent, "Default Subtitles"))
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

        // Parse all styles.
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

        // Preserve parsed styles; add the default style only when no styles were found.
        builder = styles.Count > 0 ? builder.WithStyles(styles) : builder.WithAddDefaultStyle();

        // Parse all dialogue lines and sort them by start time.
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

    /// <summary>Reads an ASS file directly and parses it into a builder.</summary>
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
    /// <param name="output">Optional console output port; defaults to the global facade.</param>
    /// <returns>A builder pre-populated with default script info and subtitle lines.</returns>
    public static AssSubBuilder FromWorkflow(SubtitleWorkflowContext context, IConsoleOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        output ??= ConsoleServices.Output;
        var builder = new AssSubBuilder().WithDefaultValues();
        if (!string.IsNullOrEmpty(context.Config.InputFilePath))
            builder = builder.WithTitle(Path.GetFileNameWithoutExtension(context.Config.InputFilePath));

        // Prefer custom styles from workflow state (edited by the Studio frontend through the intermediate file).
        // Fall back to the built-in Default and Sub styles when empty. Always ensure Default exists,
        // and ensure Sub exists for bilingual layouts.
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
            // Keep zero-duration sentences (End == Start), such as instant words from alignment.
            // Give them a minimum duration of 80 ms to prevent losing the final word.
            if (sentence.End <= sentence.Start)
                sentence.End = sentence.Start + 80;

            // Use speaker labels only when diarization completed successfully; otherwise they are placeholders and are omitted.
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

            // Prefer TranslatedText when it is available.
            // Monolingual output uses one Default-style line; bilingual output uses the original on the Default-style upper line
            // (MarginV 100) and the translation on the bottom-aligned Sub-style line (MarginV 28), matching Theme.ass.
            // In KaraokeMode, translated sentences remain visible; interpolate timings and allocate more time to longer syllables for word-level \K tags.
            // Prefix the main/original line with the speaker label and write the ASS Name field on every line.
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
    /// Builds dialogue lines. The style parameter is the fallback style name; when <paramref name="useSentenceStyle"/> is true,
    /// prefer the style bound to <see cref="Sentence.Style"/> (assigned per line by Studio).
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
                // ScriptMissing words are real words from the transcription or script and must remain visible.
                // Skipping them would silently drop unmatched words at the end of a sentence.
                // fillGapWithEllipsis applies only to gaps with no word content; do not skip the word itself here.
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

    /// <summary>Combines all settings into a complete AssSub subtitle document.</summary>
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

    // Regular expressions for the Script Info block.
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
