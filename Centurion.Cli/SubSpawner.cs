using Centurion.Core.Models;
using Centurion.Core.Operators;
using Centurion.Core.Operators.Payload;
using System.Text.RegularExpressions;
using Centurion.Core.Operators.Results;

namespace Centurion.Core;

/// <summary>
/// ASS字幕生成枚举标记
/// </summary>
[Flags]
public enum AssSubSpawnerOptions
{
    /// <summary>从SRT字幕文件生成</summary>
    Srt = 1 << 0,

    /// <summary>从音频文件语音识别生成</summary>
    Media = 1 << 1,
}

/// <summary>
/// 字幕生成器：读取SRT/音频，自动生成标准ASS字幕文档
/// 底层复用FFmpeg音频转换算子、Whisper语音识别算子，使用AssSubBuilder流式构建字幕
/// </summary>
public static class AssSubSpawner
{
    /// <summary>SRT时间戳正则匹配：00:00:00,000 --> 00:00:00,000</summary>
    private static readonly Regex TimeStampReg =
        new(@"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})", RegexOptions.Compiled);

    /// <summary>SRT序号行正则：纯数字行</summary>
    private static readonly Regex IndexReg = new(@"^\d+$", RegexOptions.Compiled);

    /// <summary>
    /// 统一入口：根据配置与输入文件生成完整ASS字幕
    /// </summary>
    /// <param name="options">生成模式配置标记</param>
    /// <param name="path">输入文件完整路径(SRT/音频)</param>
    /// <param name="modelName">要求的模型名称</param>
    /// <param name="langCode">媒体的语言</param>
    /// <param name="initialPrompt">输入的Whisper提示词</param>
    /// <param name="ct">异步取消令牌</param>
    /// <returns>构建完成的ASS字幕文档实例</returns>
    public static async Task<AssSub> AssSpawnerAsync(
        AssSubSpawnerOptions options,
        string path,
        string modelName,
        string langCode,
        string? initialPrompt = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ct.ThrowIfCancellationRequested();
        if ((options & AssSubSpawnerOptions.Srt) != 0)
            return await SpawnSrtAsync(path, ct);
        if ((options & AssSubSpawnerOptions.Media) != 0)
            return await SpawnAudioAsync(options, path, modelName, langCode, initialPrompt, ct);
        throw new ArgumentException("当前传入的生成配置不受支持", nameof(options));
    }

    /// <summary>
    /// 读取SRT文件，解析时间轴与字幕文本，通过Builder生成ASS
    /// </summary>
    /// <param name="path">SRT文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>ASS字幕文档</returns>
    private static async Task<AssSub> SpawnSrtAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 异步读取SRT全文
        var srtText = await File.ReadAllTextAsync(path, ct);
        var lines = srtText.Split(["\r\n", "\n"], StringSplitOptions.None);

        // 使用Builder构建，自带默认样式，无需手动管理集合
        var subBuilder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();

        var index = 0;
        while (index < lines.Length)
        {
            ct.ThrowIfCancellationRequested();

            // 匹配序号行
            if (IndexReg.IsMatch(lines[index]))
            {
                index++;
                // 读取时间轴行
                if (index < lines.Length && TimeStampReg.IsMatch(lines[index]))
                {
                    var match = TimeStampReg.Match(lines[index]);
                    // 解析起始毫秒
                    var startTime = (long)new TimeSpan(
                        0,
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value)
                    ).TotalMilliseconds;
                    // 解析结束毫秒
                    var endTime = (long)new TimeSpan(
                        0,
                        int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value),
                        int.Parse(match.Groups[7].Value),
                        int.Parse(match.Groups[8].Value)
                    ).TotalMilliseconds;

                    index++;
                    // 读取多行字幕文本
                    var textLines = new List<string>();
                    while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                    {
                        textLines.Add(lines[index]);
                        index++;
                    }

                    var content = string.Join(@"\N", textLines);

                    // 流式创建Dialogue字幕行并加入文档
                    var dialogue = new AssSubLineBuilder()
                        .WithComment(false)
                        .WithLayer(0)
                        .WithStart(startTime)
                        .WithEnd(endTime)
                        .WithStyle("Default")
                        .WithName("")
                        .WithMarginL(0)
                        .WithMarginR(0)
                        .WithMarginV(0)
                        .WithEffect("")
                        .WithText(content)
                        .Build();

                    subBuilder.Lines.Add(dialogue);
                }
            }
            else
            {
                index++;
            }
        }

        return subBuilder.Build();
    }

    /// <summary>
    /// 音频转字幕：FFmpeg转标准16K单声道WAV → Whisper语音识别 → 构建ASS字幕
    /// 支持内存流无临时文件 / 磁盘临时文件两种模式
    /// </summary>
    /// <param name="options">生成配置</param>
    /// <param name="audioPath">原始音频路径</param>
    /// <param name="modelName">要求的模型名称</param>
    /// <param name="langCode">媒体的语言</param>
    /// <param name="initialPrompt"></param>
    /// <param name="ct">取消令牌</param>
    /// <returns>完整ASS字幕文档</returns>
    private static async Task<AssSub> SpawnAudioAsync(
        AssSubSpawnerOptions options,
        string audioPath,
        string modelName,
        string langCode, // 新增,
        string? initialPrompt,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<TokenInfo> tokens;

        var tempWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        try
        {
            using var ffmpegOp = new FFmpegOperator();
            var ffmpegPayload = new FFmpegConvertPayload
            {
                InputFilePath = audioPath,
                OutputFilePath = tempWav
            };
            var ffmpegReq = new OperatorsRequest<FFmpegConvertPayload> { Payload = ffmpegPayload };
            await ffmpegOp.SendRequestAsync<FFmpegFileResult, FFmpegConvertPayload>(ffmpegReq, ct);

            using var whisperOp = new WhisperCliOperator(modelName);
            var whisperPayload = new WhisperTranscribePayload
            {
                FilePath = ffmpegPayload.OutputFilePath,
                Language = langCode,
                InitialPrompt = initialPrompt   // 新增
            };
            var whisperReq = new OperatorsRequest<WhisperTranscribePayload> { Payload = whisperPayload };
            tokens = await whisperOp
                .SendRequestAsync<List<TokenInfo>, WhisperTranscribePayload>(whisperReq, ct);
        }
        finally
        {
            // 强制清理临时文件
            if (File.Exists(tempWav)) File.Delete(tempWav);
            if (File.Exists($"{tempWav}.json")) File.Delete($"{tempWav}.json");

        }
        if (tokens.Count == 0)
            throw new InvalidOperationException("No valid audio segments found.");

        // 因为 TokenInfo 已经是词级片段，直接转换为 WordTiming 列表
        var wordTimings = tokens
            .Select(token => new WordTiming
            {
                Text = FilterText(token.Text),
                Start = token.Offsets.From,
                End = token.Offsets.To
            })
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();

        if (wordTimings.Count == 0)
            throw new InvalidOperationException("No valid word timings after filtering.");

        using var mergeOp = new WordMergeOperator();
        var mergePayload = new WordMergePayload
        {
            Words = wordTimings,
            Language = langCode
        };
        var mergeReq = new OperatorsRequest<WordMergePayload> { Payload = mergePayload };
        var mergeResult = await mergeOp.SendRequestAsync<WordMergeResult, WordMergePayload>(mergeReq, ct);
        var sentences = mergeResult.Sentences;

        // 构建 ASS 字幕
        var subBuilder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();
        foreach (var sent in sentences)
        {
            var startMs = (long)sent.Start;
            var endMs = (long)sent.End;
            var dialogue = new AssSubLineBuilder()
                .WithComment(false)
                .WithLayer(0)
                .WithStart(startMs)
                .WithEnd(endMs)
                .WithStyle("Default")
                .WithName("")
                .WithMarginL(0)
                .WithMarginR(0)
                .WithMarginV(0)
                .WithEffect("")
                .WithText(sent.Text)
                .Build();
            subBuilder.Lines.Add(dialogue);
        }

        return subBuilder.Build();
    }
    
    private static readonly Regex InvalidCharRegex = new(@"[^\p{L}\p{N}\s.,!?;:()\[\]\-''""「」『』【】《》〈〉…—～，。！？；：“”‘’（）]", RegexOptions.Compiled);

    private static string FilterText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : InvalidCharRegex.Replace(text, "");
    }
}