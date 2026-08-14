using System.Text;
using System.Text.RegularExpressions;
using Centurion.Core.Models;
using Centurion.Core.Operators;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;
using Centurion.Core.Services;
using Centurion.Core.Services.Dto;
using Centurion.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Centurion.Core;

/// <summary>
/// ASS字幕生成器：读取SRT/音频，自动生成标准ASS字幕文档。
/// 依赖由 DI 容器注入，日志通过 ConsoleServices 输出，本地化由 IStringLocalizer 提供。
/// </summary>
public class AssSubSpawner
{
    private readonly SpeakerDiarizationService _diarService;
    private readonly SatSplitService _satService;
    private readonly GentleService _gentleService;
    private readonly FFmpegOperator _ffmpegOperator;
    private readonly IPythonInterop _pythonInterop;
    private readonly IStringLocalizer<Localization> _localizer;
    private readonly IServiceProvider _serviceProvider;

    // ---------- 正则表达式（线程安全） ----------
    private static readonly Regex TimeStampReg =
        new(@"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})", RegexOptions.Compiled);

    private static readonly Regex IndexReg = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex InvalidCharRegex = new(@"[^\p{L}\p{N}\s\p{P}]", RegexOptions.Compiled);

    /// <summary>
    /// 构造函数，注入所需服务。
    /// </summary>
    public AssSubSpawner(
        SpeakerDiarizationService diarService,
        SatSplitService satService,
        GentleService gentleService,
        FFmpegOperator ffmpegOperator,
        IPythonInterop pythonInterop,
        IStringLocalizer<Localization> localizer,
        IServiceProvider serviceProvider)
    {
        _diarService = diarService ?? throw new ArgumentNullException(nameof(diarService));
        _satService = satService ?? throw new ArgumentNullException(nameof(satService));
        _gentleService = gentleService ?? throw new ArgumentNullException(nameof(gentleService));
        _ffmpegOperator = ffmpegOperator ?? throw new ArgumentNullException(nameof(ffmpegOperator));
        _pythonInterop = pythonInterop ?? throw new ArgumentNullException(nameof(pythonInterop));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    // ---------- 公共入口 ----------
    public async Task<AssSub> AssSpawnerAsync(
        AssSubSpawnerOptions options,
        string path,
        SubtitleGenerationOptions genOptions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ct.ThrowIfCancellationRequested();

        ConsoleServices.Output?.WriteLine(_localizer["GeneratingSubtitles", path, options]);

        if ((options & AssSubSpawnerOptions.Srt) != 0)
            return await SpawnSrtAsync(path, ct);

        if ((options & AssSubSpawnerOptions.Media) != 0)
            return await SpawnAudioAsync(
                path,
                genOptions.ModelName,
                genOptions.Language,
                genOptions.InitialPrompt,
                genOptions,
                ct);

        throw new ArgumentException(_localizer["UnsupportedOptions"], nameof(options));
    }

    // ---------- SRT 处理 ----------
    private async Task<AssSub> SpawnSrtAsync(string path, CancellationToken ct)
    {
        ConsoleServices.Output?.WriteLine(_localizer["ProcessingSrt", path]);
        ct.ThrowIfCancellationRequested();

        var srtText = await File.ReadAllTextAsync(path, ct);
        var lines = srtText.Split(["\r\n", "\n"], StringSplitOptions.None);

        var subBuilder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();

        var index = 0;
        while (index < lines.Length)
        {
            ct.ThrowIfCancellationRequested();

            if (IndexReg.IsMatch(lines[index]))
            {
                index++;
                if (index < lines.Length && TimeStampReg.IsMatch(lines[index]))
                {
                    var match = TimeStampReg.Match(lines[index]);
                    var startTime = (long)new TimeSpan(
                        0,
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value)
                    ).TotalMilliseconds;

                    var endTime = (long)new TimeSpan(
                        0,
                        int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value),
                        int.Parse(match.Groups[7].Value),
                        int.Parse(match.Groups[8].Value)
                    ).TotalMilliseconds;

                    index++;
                    var textLines = new List<string>();
                    while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                    {
                        textLines.Add(lines[index]);
                        index++;
                    }

                    var content = string.Join(@"\N", textLines);
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

        ConsoleServices.Output?.WriteLine(_localizer["SrtDone", subBuilder.Lines.Count]);
        return subBuilder.Build();
    }

    // ---------- 音频处理 ----------
    private async Task<AssSub> SpawnAudioAsync(
        string audioPath,
        string modelName,
        string langCode,
        string? initialPrompt,
        SubtitleGenerationOptions genOptions,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<TokenInfo> tokens;

        var tempWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        string? transcriptPath = null;

        try
        {
            ConsoleServices.Output?.WriteLine(_localizer["ProcessingAudio", audioPath, modelName, langCode]);

            // ---------- 1. FFmpeg 转 16kHz 单声道 WAV ----------
            var ffmpegPayload = new FFmpegConvertPayload
            {
                InputFilePath = audioPath,
                OutputFilePath = tempWav
            };
            var ffmpegReq = new OperatorsRequest<FFmpegConvertPayload> { Payload = ffmpegPayload };
            await _ffmpegOperator.SendRequestAsync<FFmpegFileResult, FFmpegConvertPayload>(ffmpegReq, ct);
            ConsoleServices.Output?.WriteLine(_localizer["FFmpegDone", tempWav]);

            // ---------- 2. Whisper 转录 ----------
            ConsoleServices.Output?.WriteLine(_localizer["WhisperStart"]);
            
            // 使用 DI 容器动态创建 ModelManager（传入动态参数）
            var modelManager = ActivatorUtilities.CreateInstance<ModelManager>(
                _serviceProvider,
                modelName,
                ModelRegistry.WhisperModels,
                "whisper");
            await modelManager.EnsureModelAvailableAsync();
            
            using var whisperOp = new WhisperCliOperator(modelManager, _serviceProvider.GetRequiredService<IBinaryLocator>());
                
            var whisperPayload = new WhisperTranscribePayload
            {
                FilePath = tempWav,
                Language = langCode,
                InitialPrompt = initialPrompt
            };
            var whisperReq = new OperatorsRequest<WhisperTranscribePayload> { Payload = whisperPayload };
            tokens = await whisperOp
                .SendRequestAsync<List<TokenInfo>, WhisperTranscribePayload>(whisperReq, ct);

            if (tokens.Count == 0)
            {
                ConsoleServices.Output?.WriteError(_localizer["WhisperNoSegments"]);
                throw new InvalidOperationException(_localizer["WhisperNoSegments"]);
            }

            // ---------- 3. 过滤特殊令牌并获取词级时间 ----------
            var wordTimings = tokens
                .Where(token => !token.IsSpecialToken())
                .Select(token => new WordTiming
                {
                    Text = FilterText(token.Text),
                    Start = token.Offsets.From,
                    End = token.Offsets.To
                })
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .ToList();

            if (wordTimings.Count == 0)
            {
                ConsoleServices.Output?.WriteError(_localizer["NoWordTimings"]);
                throw new InvalidOperationException(_localizer["NoWordTimings"]);
            }

            ConsoleServices.Output?.WriteLine(_localizer["WhisperDone", wordTimings.Count]);

            // ---------- 4. 高精度模式（Gentle） ----------
            if (genOptions.UseGentle)
            {
                ConsoleServices.Output?.WriteLine(_localizer["GentleStart"]);
                transcriptPath = Path.GetTempFileName();
                var plainText = string.Join(" ", wordTimings.Select(w => w.Text));
                await File.WriteAllTextAsync(transcriptPath, plainText, ct);

                var gentleResult = await _gentleService.AlignAsync(tempWav, transcriptPath, ct);
                if (gentleResult.Words.Count > 0)
                {
                    wordTimings = gentleResult.Words
                        .Select(w => new WordTiming
                        {
                            Text = w.Word,
                            Start = w.Start * 1000,
                            End = w.End * 1000
                        })
                        .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                        .ToList();

                    if (wordTimings.Count == 0)
                        throw new InvalidOperationException(_localizer["GentleNoWords"]);
                    ConsoleServices.Output?.WriteLine(_localizer["GentleDone", wordTimings.Count]);
                }
                else
                {
                    ConsoleServices.Output?.WriteWarning(_localizer["GentleFallback"]);
                }
            }

            // ---------- 5. 说话人分割 ----------
            if (genOptions.NumSpeakers >= 0)
            {
                ConsoleServices.Output?.WriteLine(genOptions.NumSpeakers != 0
                    ? _localizer["DiarizationStart", genOptions.NumSpeakers]
                    : _localizer["DiarizationStart", "auto"]);
                var pythonPath = await _pythonInterop.LocatePythonAsync(ct);
                await _diarService.StartAsync(pythonPath, ct);
                var diarizationSegments = await _diarService.DiarizeAsync(tempWav, genOptions.NumSpeakers, ct);
                ConsoleServices.Output?.WriteLine(_localizer["DiarizationDone", diarizationSegments.Count]);
            }

            // ---------- 6. SAT 分句 ----------
            ConsoleServices.Output?.WriteLine(_localizer["SatStart", genOptions.MaxLength, genOptions.TargetLength]);
            var pythonPath2 = await _pythonInterop.LocatePythonAsync(ct);
            await _satService.StartAsync(pythonPath2, ct);
            var groups = await _satService.SplitAsync(
                wordTimings,
                genOptions.MaxLength,
                genOptions.TargetLength,
                genOptions.SpreadRange,
                ct);

            if (groups == null || groups.Count == 0)
            {
                ConsoleServices.Output?.WriteError(_localizer["SatEmpty"]);
                throw new InvalidOperationException(_localizer["SatEmpty"]);
            }
            ConsoleServices.Output?.WriteLine(_localizer["SatDone", groups.Count]);

            // ---------- 7. 构建 ASS 字幕 ----------
            ConsoleServices.Output?.WriteLine(_localizer["BuildingAss"]);
            var subBuilder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();

            foreach (var group in groups)
            {
                var startMs = (long)group.Start;
                var endMs = (long)group.End;
                string text = genOptions.Karaoke
                    ? BuildKaraokeFromWords(group)
                    : SubTools.NormalizeSpaces(group.Text);

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
                    .WithText(text)
                    .Build();

                subBuilder.Lines.Add(dialogue);
            }

            ConsoleServices.Output?.WriteLine(_localizer["AssDone", subBuilder.Lines.Count]);
            return subBuilder.Build();
        }
        catch (Exception ex)
        {
            ConsoleServices.Output?.WriteError(_localizer["AudioProcessingError", ex.Message]);
            throw;
        }
        finally
        {
            // ---------- 清理临时文件 ----------
            if (File.Exists(tempWav)) File.Delete(tempWav);
            if (File.Exists($"{tempWav}.json")) File.Delete($"{tempWav}.json");
            if (transcriptPath != null && File.Exists(transcriptPath)) File.Delete(transcriptPath);
        }
    }

    // ---------- 辅助方法 ----------
    private static string BuildKaraokeFromWords(SatSplitResult group)
    {
        var words = group.Words;
        if (words == null || words.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var durationMs = word.End - word.Start;
            if (durationMs < 10) durationMs = 10;
            var durationCs = (long)((durationMs + 5) / 10);
            if (durationCs == 0) durationCs = 1;

            var text = SubTools.NormalizeSpaces(word.Text);
            if (string.IsNullOrEmpty(text)) continue;

            sb.Append($"{{\\K{durationCs}}}{text}");
            if (i < words.Count - 1)
                sb.Append(' ');
        }

        return sb.ToString();
    }

    private static string FilterText(string text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : InvalidCharRegex.Replace(text, "");
}