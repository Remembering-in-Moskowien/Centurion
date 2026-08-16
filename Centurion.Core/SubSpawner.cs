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
using MfaWord = Centurion.Core.Operators.Payload.MfaWord;

namespace Centurion.Core;

/// <summary>
/// ASS字幕生成器：读取SRT/音频，自动生成标准ASS字幕文档。
/// 依赖由 DI 容器注入，日志通过 ConsoleServices 输出，本地化由 IStringLocalizer 提供。
/// </summary>
public class AssSubSpawner
{
    private readonly SpeakerDiarizationService _diarService;
    private readonly SatSplitService _satService;
    private readonly MfaCliOperator _mfaOperator;
    private readonly FFmpegOperator _ffmpegOperator;
    private readonly IPythonInterop _pythonInterop;
    private readonly IStringLocalizer<Localization> _localizer;
    private readonly IServiceProvider _serviceProvider;

    private const int Paddingms = 1000;

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
        SatSplitService satService,
        MfaCliOperator mfaOperator,
        FFmpegOperator ffmpegOperator,
        IPythonInterop pythonInterop,
        IStringLocalizer<Localization> localizer,
        IServiceProvider serviceProvider)
    {
        _diarService = diarService ?? throw new ArgumentNullException(nameof(diarService));
        _satService = satService ?? throw new ArgumentNullException(nameof(satService));
        _mfaOperator = mfaOperator ?? throw new ArgumentNullException(nameof(mfaOperator));
        _ffmpegOperator = ffmpegOperator ?? throw new ArgumentNullException(nameof(ffmpegOperator));
        _pythonInterop = pythonInterop ?? throw new ArgumentNullException(nameof(pythonInterop));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 检查并准备 Conda 环境（可被 GUI 程序在启动时调用）。
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
                "ctranslate2",
                "certifi",
                "diarize");
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
        string path,
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
            ConsoleServices.Output?.WriteLine(_localizer["ProcessingAudio", path, modelName, langCode]);

            // 0. 确保 Conda 环境已就绪
            await EnsureCondaEnvironmentAsync(ct);

            // 1. FFmpeg 转 16kHz 单声道 WAV
            var ffmpegPayload = new FFmpegConvertPayload
            {
                InputFilePath = path,
                OutputFilePath = tempWav
            };
            var ffmpegReq = new OperatorsRequest<FFmpegConvertPayload> { Payload = ffmpegPayload };
            await _ffmpegOperator.SendRequestAsync<FFmpegConvertResult, FFmpegConvertPayload>(ffmpegReq, ct);
            ConsoleServices.Output?.WriteLine(_localizer["FFmpegDone", tempWav]);

            // 2. Whisper 转录
            ConsoleServices.Output?.WriteLine(_localizer["WhisperStart"]);
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
                ConsoleServices.Output?.WriteError(_localizer["WhisperNoSegments"]);
                throw new InvalidOperationException(_localizer["WhisperNoSegments"]);
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
                ConsoleServices.Output?.WriteError(_localizer["NoWordTimings"]);
                throw new InvalidOperationException(_localizer["NoWordTimings"]);
            }

            ConsoleServices.Output?.WriteLine(_localizer["WhisperDone", wordTimings.Count]);

            // 4. 说话人分割（已注释，保持单说话人模式）
            // if (genOptions.NumSpeakers >= 0) { ... }

            // 5. SAT 分句
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

            // ---------- 6. 高精度模式（MFA） ----------
            // ---------- 6. 高精度模式（MFA） ----------
            if (genOptions.UseMfa)
            {
                ConsoleServices.Output?.WriteLine(_localizer["MFAStart"]);

                // 6.1 切割音频
                var segmentDir = Path.Combine(taskTempDir, "segments");
                Directory.CreateDirectory(segmentDir);

                var segments = groups.Select(g =>
                {
                    var start = Math.Max(0, g.Start - Paddingms);
                    var end = g.End + Paddingms;
                    return (StartMs: (long)start, EndMs: (long)end);
                }).ToList();

                var splitPayload = new FFmpegSplitPayload
                {
                    InputFilePath = tempWav,
                    Segments = segments,
                    OutputDirectory = segmentDir
                };
                var splitRequest = new OperatorsRequest<FFmpegSplitPayload> { Payload = splitPayload };
                var splitResult =
                    await _ffmpegOperator.SendRequestAsync<FFmpegSplitResult, FFmpegSplitPayload>(splitRequest, ct);
                ConsoleServices.Output?.WriteLine(_localizer["SplitDone", splitResult.OutputFiles.Count, segmentDir]);

                // 6.2 验证并生成归一化文本
                var validSegments = new List<(int Index, string AudioPath, string LabPath, SatSplitResult Group)>();
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    var baseName = $"segment_{i + 1:D4}";
                    var audioPath = Path.Combine(segmentDir, $"{baseName}.wav");
                    var labPath = Path.Combine(segmentDir, $"{baseName}.lab");

                    if (!File.Exists(audioPath))
                    {
                        ConsoleServices.Output?.WriteWarning(_localizer["AudioFileMissing", i + 1, audioPath]);
                        continue;
                    }

                    var normalizedText = NormalizeForMfa(group.Text);
                    if (string.IsNullOrWhiteSpace(normalizedText))
                        normalizedText = "sil";
                    await File.WriteAllTextAsync(labPath, normalizedText, Encoding.UTF8, ct);
                    validSegments.Add((i, audioPath, labPath, group));
                }

                if (validSegments.Count == 0)
                {
                    ConsoleServices.Output?.WriteWarning(_localizer["NoValidSegments"]);
                }
                else
                {
                    // 6.3 构建平铺输入目录
                    var mfaInputDir = Path.Combine(taskTempDir, "mfa_input");
                    Directory.CreateDirectory(mfaInputDir);

                    foreach (var (idx, audioPath, labPath, group) in validSegments)
                    {
                        var baseName = $"segment_{idx + 1:D4}";
                        File.Copy(audioPath, Path.Combine(mfaInputDir, $"{baseName}.wav"), true);
                        File.Copy(labPath, Path.Combine(mfaInputDir, $"{baseName}.lab"), true);
                    }

                    var mfaOutputDir = Path.Combine(taskTempDir, "mfa_output");
                    Directory.CreateDirectory(mfaOutputDir);

                    try
                    {
                        // 6.4 调用 MFA 批量对齐
                        var mfaResults = await _mfaOperator.AlignCorpusAsync(mfaInputDir, mfaOutputDir, ct);

                        // 6.5 映射 MFA 结果到全局时间轴
                        var allMfaWords = new List<MfaWord>();
                        foreach (var kvp in mfaResults)
                        {
                            if (!int.TryParse(kvp.Key.Replace("segment_", ""), out var idx))
                                continue;
                            idx--;
                            if (idx < 0 || idx >= groups.Count)
                                continue;

                            var group = groups[idx];
                            var originalStartMs = group.Start;
                            var cutStartMs = Math.Max(0, originalStartMs - Paddingms);

                            foreach (var w in kvp.Value)
                            {
                                var globalStartMs = (long)(w.Start * 1000 + cutStartMs);
                                var globalEndMs = (long)(w.End * 1000 + cutStartMs);
                                if (globalEndMs <= originalStartMs || globalStartMs >= group.End)
                                    continue;
                                if (globalStartMs < originalStartMs) globalStartMs = (long)originalStartMs;
                                if (globalEndMs > group.End) globalEndMs = (long)group.End;
                                if (globalStartMs < globalEndMs)
                                    allMfaWords.Add(new MfaWord
                                    {
                                        Word = w.Word,
                                        Start = globalStartMs / 1000.0,
                                        End = globalEndMs / 1000.0
                                    });
                            }
                        }

                        if (allMfaWords.Count > 0)
                        {
                            // 1. 替换 wordTimings（备用）
                            wordTimings = allMfaWords.Select(w => new WordTiming
                            {
                                Text = w.Word,
                                Start = w.Start * 1000,
                                End = w.End * 1000,
                                Speaker = "UNKNOWN"
                            }).ToList();

                            // 2. 更新每个句子的边界和词级时间
                            for (int i = 0; i < groups.Count; i++)
                            {
                                var group = groups[i];
                                var originalStart = group.Start;
                                var originalEnd = group.End;

                                // 2.1 查找属于该句的MFA词
                                var mfaWordsForGroup = allMfaWords
                                    .Where(w => w.Start * 1000 >= originalStart - 100 && w.End * 1000 <= originalEnd + 100)
                                    .ToList();

                                if (mfaWordsForGroup.Any())
                                {
                                    // 2.2 更新句子边界（融合策略）
                                    var mfaStart = mfaWordsForGroup.Min(w => w.Start * 1000);
                                    var mfaEnd = mfaWordsForGroup.Max(w => w.End * 1000);
                                    group.Start = Math.Min(originalStart, mfaStart);
                                    group.End = Math.Max(originalEnd, mfaEnd);

                                    // 2.3 【核心改动】更新词级时间（用于卡拉OK）
                                    // 从group中提取原始的SAT词列表（假设group.Words已包含文本）
                                    var satWordTexts = group.Words?.Select(w => w.Text).ToList() ?? new List<string>();
                                    // 如果group.Words为空，则从group.Text按空格分割
                                    if (!satWordTexts.Any())
                                    {
                                        satWordTexts = group.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                    }

                                    // 执行词级映射
                                    var mappedWordTimings = MapMfaToSatWords(satWordTexts, mfaWordsForGroup);

                                    // 更新group.Words
                                    group.Words = mappedWordTimings;
                                }
                            }

                            ConsoleServices.Output?.WriteLine(_localizer["MFADone", allMfaWords.Count]);
                        }
                        else
                        {
                            ConsoleServices.Output?.WriteWarning(_localizer["MFANoWords"]);
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleServices.Output?.WriteWarning(_localizer["MFAError", ex.Message]);
                    }
                    finally
                    {
                        // 清理临时目录（调试时可注释）
                        try
                        {
                            if (Directory.Exists(mfaInputDir)) Directory.Delete(mfaInputDir, true);
                        }
                        catch
                        {
                            // ignored
                        }

                        try
                        {
                            if (Directory.Exists(mfaOutputDir)) Directory.Delete(mfaOutputDir, true);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }

            // ---------- 7. 构建 ASS 字幕 ----------
            ConsoleServices.Output?.WriteLine(_localizer["BuildingAss"]);
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
            // 清理整个任务临时目录
            if (Directory.Exists(taskTempDir))
                try
                {
                    Directory.Delete(taskTempDir, true);
                    ConsoleServices.Output?.WriteLine(_localizer["TaskTempDeleted", taskTempDir]);
                }
                catch (Exception ex)
                {
                    ConsoleServices.Output?.WriteWarning(_localizer["TaskTempDeleteError", ex.Message]);
                }
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
    
    /// <summary>
    /// 将MFA词列表的时间戳映射到SAT词列表上。
    /// 使用字符串近似匹配来处理因分词、缩写等导致的数量不一致问题。
    /// </summary>
    private List<WordTiming> MapMfaToSatWords(List<string> satWords, List<MfaWord> mfaWords)
    {
        var mappedTimings = new List<WordTiming>();
        int mfaIndex = 0;

        foreach (var satWord in satWords)
        {
            // 跳过空词
            if (string.IsNullOrWhiteSpace(satWord))
                continue;

            // 从当前MFA位置开始，尝试匹配SAT词
            WordTiming? bestMatch = null;
            int bestMatchIndex = -1;

            // 向前查找（最多尝试10个MFA词，可调整）
            for (int i = mfaIndex; i < Math.Min(mfaIndex + 10, mfaWords.Count); i++)
            {
                var mfaWord = mfaWords[i];
                // 检查MFA词是否包含SAT词（忽略大小写）
                if (mfaWord.Word.Contains(satWord, StringComparison.OrdinalIgnoreCase) ||
                    satWord.Contains(mfaWord.Word, StringComparison.OrdinalIgnoreCase))
                {
                    bestMatch = new WordTiming
                    {
                        Text = satWord, // 保留原始SAT文本
                        Start = mfaWord.Start * 1000, // 转为毫秒
                        End = mfaWord.End * 1000,
                        Speaker = "UNKNOWN"
                    };
                    bestMatchIndex = i;
                    break;
                }
            }

            if (bestMatch != null)
            {
                mappedTimings.Add(bestMatch);
                mfaIndex = bestMatchIndex + 1; // 从下一个MFA词继续匹配
            }
            else
            {
                // 如果没有匹配到，使用SAT词本身的文本，但时间戳暂时空缺
                // 这里使用前一个词或后一个词的时间进行插值，或标记为未对齐
                // 简单起见，此处保留原SAT词的时间（如果有），否则设为0
                mappedTimings.Add(new WordTiming
                {
                    Text = satWord,
                    Start = mappedTimings.LastOrDefault()?.End ?? 0,
                    End = mappedTimings.LastOrDefault()?.End ?? 0,
                    Speaker = "UNKNOWN"
                });
                // 不移动mfaIndex，继续尝试匹配下一个SAT词
            }
        }

        return mappedTimings;
    }

    private static string FilterText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : InvalidCharRegex.Replace(text, "");
    }

    // ---------- MFA 文本归一化 ----------
    private static string NormalizeForMfa(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Trim();

        // 1. 展开常见缩写（可按需扩展）
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
            { "i.e.", "that is" }
        };
        foreach (var kvp in abbreviations)
            text = Regex.Replace(text, Regex.Escape(kvp.Key), kvp.Value, RegexOptions.IgnoreCase);

        // 2. 处理货币: $100 -> one hundred dollars
        text = Regex.Replace(text, @"\$(\d+)", match =>
        {
            var num = int.Parse(match.Groups[1].Value);
            return $"{NumberToWords(num)} dollars";
        });

        // 3. 处理百分比: 99% -> ninety nine percent
        text = Regex.Replace(text, @"(\d+)%", match =>
        {
            var num = int.Parse(match.Groups[1].Value);
            return $"{NumberToWords(num)} percent";
        });

        // 4. 处理普通数字（年份、数量等）
        text = Regex.Replace(text, @"\b(\d+)\b", match =>
        {
            var num = int.Parse(match.Groups[1].Value);
            return NumberToWords(num);
        });

        // 5. 移除多余标点，仅保留字母、空格、句号、问号、感叹号（MFA 需要句子边界）
        text = Regex.Replace(text, @"[^a-zA-Z\s.?!]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    // 简单的数字转英文（支持0~9999，可根据需要扩展）
    private static string NumberToWords(int number)
    {
        if (number == 0) return "zero";
        if (number < 0) return "minus " + NumberToWords(Math.Abs(number));

        var words = "";

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
            var unitsMap = new[]
            {
                "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
            };
            var tensMap = new[]
                { "zero", "ten", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

            if (number < 20)
            {
                words += unitsMap[number];
            }
            else
            {
                words += tensMap[number / 10];
                if (number % 10 > 0)
                    words += "-" + unitsMap[number % 10];
            }
        }

        return words.Trim();
    }
}