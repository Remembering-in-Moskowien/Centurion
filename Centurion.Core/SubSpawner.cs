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
using MfaAlignPayload = Centurion.Core.Operators.Payload.MfaAlignPayload;
using MfaWord = Centurion.Core.Operators.Payload.MfaWord;

namespace Centurion.Core;

/// <summary>
/// ASS字幕生成器：读取SRT/音频，自动生成标准ASS字幕文档。
/// 依赖由 DI 容器注入，日志通过 ConsoleServices 输出。
/// </summary>
public class AssSubSpawner
{
    // 注意：说话人分割和 MFA 已被注释，因此以下两个服务虽注入但不再使用
    private readonly SpeakerDiarizationService _diarService;
    private readonly CatalystSplitService _catalystService;
    private readonly MfaCliOperator _mfaOperator;
    private readonly FFmpegOperator _ffmpegOperator;
    private readonly IPythonInterop _pythonInterop;
    private readonly IServiceProvider _serviceProvider;

    private const int Paddingms = 300;

    // ---------- 正则表达式（线程安全） ----------
    private static readonly Regex TimeStampReg =
        new(@"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})", RegexOptions.Compiled);

    private static readonly Regex IndexReg = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex InvalidCharRegex = new(@"[^\p{L}\p{N}\s\p{P}]", RegexOptions.Compiled);

    // ---------- Conda 环境就绪标志 ----------
    private static readonly SemaphoreSlim EnvLock = new(1, 1);
    private static bool _environmentReady;

    /// <summary>
    /// 构造函数，注入所需服务。
    /// </summary>
    public AssSubSpawner(
        SpeakerDiarizationService diarService,
        CatalystSplitService catalystService,
        MfaCliOperator mfaOperator,
        FFmpegOperator ffmpegOperator,
        IPythonInterop pythonInterop,
        IServiceProvider serviceProvider)
    {
        _diarService = diarService ?? throw new ArgumentNullException(nameof(diarService));
        _catalystService = catalystService ?? throw new ArgumentNullException(nameof(catalystService));
        _mfaOperator = mfaOperator ?? throw new ArgumentNullException(nameof(mfaOperator));
        _ffmpegOperator = ffmpegOperator ?? throw new ArgumentNullException(nameof(ffmpegOperator));
        _pythonInterop = pythonInterop ?? throw new ArgumentNullException(nameof(pythonInterop));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 检查并准备 Conda 环境（可被 GUI 程序在启动时调用）。
    /// 注意：由于说话人分割和 MFA 被注释，此方法可能不再需要，但保留以便未来恢复。
    /// </summary>
    public async Task EnsureCondaEnvironmentAsync(CancellationToken ct = default)
    {
        if (_environmentReady) return;

        await EnvLock.WaitAsync(ct);
        try
        {
            if (_environmentReady) return;

            await _pythonInterop.EnsureVirtualEnvironmentAsync(ct,
                "wtpsplit",
                "numpy",
                "scipy",
                "torch",
                "soundfile",
                "librosa",
                "torchcodec",
                "ctranslate2",
                "certifi",
                "wespeaker",
                "onnxruntime",
                "scikit-learn");
            _environmentReady = true;
        }
        finally
        {
            EnvLock.Release();
        }
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

        ConsoleServices.Output?.WriteLine(string.Format("Generating subtitles for: {0}, options: {1}", path, options));

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

        throw new ArgumentException("Unsupported generation options", nameof(options));
    }

    // ---------- SRT 处理 ----------
    private async Task<AssSub> SpawnSrtAsync(string path, CancellationToken ct)
    {
        ConsoleServices.Output?.WriteLine(string.Format("Processing SRT file: {0}", path));
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

        ConsoleServices.Output?.WriteLine(string.Format("SRT parsed, {0} subtitles generated", subBuilder.Lines.Count));
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

        // 创建任务专属临时目录
        var taskTempDir = Path.Combine(Environment.CurrentDirectory, "temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(taskTempDir);

        var tempWav = Path.Combine(taskTempDir, "audio.wav");

        try
        {
            ConsoleServices.Output?.WriteLine(string.Format("Processing audio: {0}, model: {1}, language: {2}", audioPath, modelName, langCode));

            // 0. 确保 Conda 环境已就绪（用于 Whisper-cli 和可能的后续扩展）
            await EnsureCondaEnvironmentAsync(ct);

            // 1. FFmpeg 转 16kHz 单声道 WAV
            var ffmpegPayload = new FFmpegConvertPayload
            {
                InputFilePath = audioPath,
                OutputFilePath = tempWav
            };
            var ffmpegReq = new OperatorsRequest<FFmpegConvertPayload> { Payload = ffmpegPayload };
            await _ffmpegOperator.SendRequestAsync<FFmpegConvertResult, FFmpegConvertPayload>(ffmpegReq, ct);
            ConsoleServices.Output?.WriteLine(string.Format("FFmpeg conversion done, temp file: {0}", tempWav));

            // 2. Whisper 转录
            ConsoleServices.Output?.WriteLine("Whisper transcription started");
            var modelManager = ActivatorUtilities.CreateInstance<ModelManager>(
                _serviceProvider,
                modelName,
                ModelRegistry.WhisperModels,
                "whisper");
            await modelManager.EnsureModelAvailableAsync();

            using var whisperOp =
                new WhisperCliOperator(modelManager, _serviceProvider.GetRequiredService<IBinaryLocator>());

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
                ConsoleServices.Output?.WriteError("Whisper returned no segments");
                throw new InvalidOperationException("Whisper returned no segments");
            }

            // 3. 过滤特殊令牌并获取词级时间
            var wordTimings = tokens
                .Where(token => !token.IsSpecialToken())
                .Select(token => new WordTiming
                {
                    Text = FilterText(token.Text),
                    Start = token.Offsets.From,
                    End = token.Offsets.To,
                    Speaker = "UNKNOWN"
                })
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .ToList();

            if (wordTimings.Count == 0)
            {
                ConsoleServices.Output?.WriteError("No valid word timings after filtering");
                throw new InvalidOperationException("No valid word timings after filtering");
            }

            ConsoleServices.Output?.WriteLine($"Whisper transcription done, {wordTimings.Count} words");

            // 4. 调用 Catalyst 分句（替代原来的 SAT）
            ConsoleServices.Output?.WriteLine(
                $"Catalyst split started, max_len: {genOptions.MaxLength}, target_len: {genOptions.TargetLength}");

            // 构造请求对象
            var splitRequest = new SentenceSplitRequest
            {
                Words = wordTimings,
                Language = langCode,
                MaxLength = genOptions.MaxLength,
                TargetLength = genOptions.TargetLength,
                SpreadRange = genOptions.SpreadRange
            };

            var groups = await _catalystService.SplitAsync(splitRequest, ct);

            if (groups == null || groups.Count == 0)
            {
                ConsoleServices.Output?.WriteError("Catalyst split returned empty result");
                throw new InvalidOperationException("Catalyst split returned empty result");
            }

            ConsoleServices.Output?.WriteLine(string.Format("Catalyst split done, {0} groups", groups.Count));

            // ---------- 5. 说话人分割（已注释） ----------
            // 为每个句子分配默认说话人
            var speakerLabels = Enumerable.Repeat("speaker_1", groups.Count).ToList();

            // ---------- 6. MFA 对齐（已注释） ----------
            // 不再调用 MFA，直接使用 Catalyst 返回的时间边界

            // ---------- 7. 构建 ASS 字幕 ----------
            ConsoleServices.Output?.WriteLine("Building ASS subtitles...");
            var subBuilder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();

            foreach (var group in groups)
            {
                var startMs = (long)group.Start;
                var endMs = (long)group.End;
                var text = genOptions.Karaoke
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

            ConsoleServices.Output?.WriteLine(string.Format("ASS subtitles built, {0} lines", subBuilder.Lines.Count));
            return subBuilder.Build();
        }
        catch (Exception ex)
        {
            ConsoleServices.Output?.WriteError(string.Format("Error during audio processing: {0}", ex.Message));
            throw;
        }
        finally
        {
            // 彻底删除任务临时目录
            if (Directory.Exists(taskTempDir))
            {
                try
                {
                    Directory.Delete(taskTempDir, true);
                    ConsoleServices.Output?.WriteLine(string.Format("Task temporary directory deleted: {0}", taskTempDir));
                }
                catch (Exception ex)
                {
                    ConsoleServices.Output?.WriteWarning(string.Format("Failed to delete task temporary directory: {0}", ex.Message));
                }
            }
        }
    }

    // ---------- 辅助方法 ----------
    private static string BuildKaraokeFromWords(SentenceSplitResult group)
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

    private static string FilterText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : InvalidCharRegex.Replace(text, "");
    }

    // ---------- MFA 文本归一化（保留供以后使用，但当前未调用） ----------
    private static string NormalizeForMfa(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Trim();

        var abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dr.", "doctor" },
            { "Mr.", "mister" },
            { "Mrs.", "missus" },
            { "Ms.", "miss" },
            { "St.", "street" },
            { "Ave.", "avenue" },
            { "U.S.", "u s" },
            { "e.g.", "for example" },
            { "i.e.", "that is" },
        };
        foreach (var kvp in abbreviations)
        {
            text = Regex.Replace(text, Regex.Escape(kvp.Key), kvp.Value, RegexOptions.IgnoreCase);
        }

        text = Regex.Replace(text, @"\$(\d+)", match =>
        {
            var num = int.Parse(match.Groups[1].Value);
            return $"{NumberToWords(num)} dollars";
        });

        text = Regex.Replace(text, @"(\d+)%", match =>
        {
            var num = int.Parse(match.Groups[1].Value);
            return $"{NumberToWords(num)} percent";
        });

        text = Regex.Replace(text, @"\b(\d+)\b", match =>
        {
            var num = int.Parse(match.Groups[1].Value);
            return NumberToWords(num);
        });

        text = Regex.Replace(text, @"[^a-zA-Z\s.?!]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    private static string NumberToWords(int number)
    {
        if (number == 0) return "zero";
        if (number < 0) return "minus " + NumberToWords(Math.Abs(number));

        string words = "";

        if (number / 1000 > 0)
        {
            words += NumberToWords(number / 1000) + " thousand ";
            number %= 1000;
        }

        if (number / 100 > 0)
        {
            words += NumberToWords(number / 100) + " hundred ";
            number %= 100;
        }

        if (number > 0)
        {
            var unitsMap = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                                   "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
            var tensMap = new[] { "zero", "ten", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

            if (number < 20)
                words += unitsMap[number];
            else
            {
                words += tensMap[number / 10];
                if ((number % 10) > 0)
                    words += "-" + unitsMap[number % 10];
            }
        }

        return words.Trim();
    }
}