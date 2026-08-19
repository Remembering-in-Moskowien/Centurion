using System.Text;
using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Centurion.Core.Tools;

namespace Centurion.Core.Operators;

/// <summary>
/// 说话人感知的 MFA 对齐算子。
/// 流程：按说话人分组 → 合并为5-30秒片段 → 生成语料库 → 调用MFA对齐 → 时间插值映射回原始句子。
/// </summary>
public class SpeakerMfaAlignmentOperator(
    CondaEnvironmentManager condaManager,
    FFmpegSplitOperator ffmpegSplit,
    ITempDirectoryManager tempManager)
    : IOperator<SpeakerMfaRequest, SpeakerMfaResponse>, IAsyncDisposable
{
    private readonly CondaEnvironmentManager _condaManager = condaManager ?? throw new ArgumentNullException(nameof(condaManager));
    private readonly FFmpegSplitOperator _ffmpegSplit = ffmpegSplit ?? throw new ArgumentNullException(nameof(ffmpegSplit));
    private readonly ITempDirectoryManager _tempManager = tempManager ?? throw new ArgumentNullException(nameof(tempManager));

    private const int MinSegmentDurationSec = 5;
    private const int MaxSegmentDurationSec = 30;
    private const int SilencePaddingMs = 100;

    private string? _mfaBinaryPath;
    private string? _openFstBinPath;
    private bool _mfaReady;
    private bool _disposed;

    public async Task EnsureTargetAvailableAsync()
    {
        if (_mfaReady)
            return;

        // 1. 通过 Conda 安装 MFA
        await _condaManager.EnsurePackagesAsync(new[] { "montreal-forced-aligner" });

        // 2. 定位 mfa 可执行文件
        var envDir = _condaManager.EnvironmentPath;
        _mfaBinaryPath = OperatingSystem.IsWindows()
            ? Path.Combine(envDir, "Scripts", "mfa.exe")
            : Path.Combine(envDir, "bin", "mfa");
        if (!File.Exists(_mfaBinaryPath))
            throw new Exception("montreal-forced-aligner installed but mfa executable not found.");

        _openFstBinPath = OperatingSystem.IsWindows()
            ? Path.Combine(envDir, "Library", "bin")
            : Path.Combine(envDir, "bin");

        // 3. 确保模型已下载（可选，MFA 自动下载）
        // 这里我们可以调用 mfa model list 等，但 MFA 会在第一次使用时自动下载，也可以提前下载
        await EnsureModelsAsync();

        _mfaReady = true;
    }

    private async Task EnsureModelsAsync()
    {
        // 检查并下载声学模型和词典（如果不存在）
        var acousticModel = "english_mfa";
        var dictionary = "english_us_mfa";

        var acousticCheck = await RunMfaCommandAsync("model list acoustic");
        if (!acousticCheck.Contains(acousticModel))
        {
            ConsoleServices.Output?.WriteLine("Downloading MFA acoustic model...");
            await RunMfaCommandAsync($"model download acoustic {acousticModel}");
        }

        var dictionaryCheck = await RunMfaCommandAsync("model list dictionary");
        if (!dictionaryCheck.Contains(dictionary))
        {
            ConsoleServices.Output?.WriteLine("Downloading MFA dictionary...");
            await RunMfaCommandAsync($"model download dictionary {dictionary}");
        }
    }

    public async Task<SpeakerMfaResponse> ProcessAsync(
        OperatorsRequest<SpeakerMfaRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        var payload = request.Payload;
        return await AlignAsync(payload, cancellationToken);
    }

    private async Task<SpeakerMfaResponse> AlignAsync(SpeakerMfaRequest payload, CancellationToken ct)
    {
        var sentences = payload.Sentences;
        if (sentences == null || sentences.Count == 0)
            return new SpeakerMfaResponse { Sentences = new List<Sentence>() };

        // 按说话人分组
        var speakerGroups = sentences
            .Select((s, idx) => new { Sentence = s, Index = idx })
            .GroupBy(x => x.Sentence.Words.FirstOrDefault()?.Speaker ?? "unknown")
            .ToDictionary(g => g.Key, g => g.ToList());

        // 全局词列表
        var globalWords = sentences.SelectMany(s => s.Words).ToList();

        await using var corpusRoot = await _tempManager.CreateTempDirectoryAsync("mfa_corpus_");
        var corpusDir = corpusRoot.Path;

        var allMfaTimings = new List<(double Start, double End)>();

        foreach (var group in speakerGroups)
        {
            var speaker = group.Key;
            var safeSpeaker = string.Concat(speaker.Where(c => char.IsLetterOrDigit(c) || c == '_'));
            if (string.IsNullOrEmpty(safeSpeaker)) safeSpeaker = "unknown";

            var speakerDir = Path.Combine(corpusDir, safeSpeaker);
            Directory.CreateDirectory(speakerDir);

            var speakerSentences = group.Value.OrderBy(x => x.Sentence.Start).Select(x => x.Sentence).ToList();

            var mfaSegments = MergeToSegments(speakerSentences);
            ConsoleServices.Output?.WriteLine($"Speaker '{speaker}': {mfaSegments.Count} MFA segments.");

            for (int i = 0; i < mfaSegments.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var segment = mfaSegments[i];
                var segName = $"seg_{i + 1:D4}";

                var segAudioPath = Path.Combine(speakerDir, $"{segName}.wav");
                await ExtractAudioSegmentWithSilenceAsync(payload.AudioFilePath, segment.StartMs, segment.EndMs, segAudioPath, ct);

                var labPath = Path.Combine(speakerDir, $"{segName}.lab");
                var labContent = string.Join(" ", segment.Words.Select(w => w.Text));
                await File.WriteAllTextAsync(labPath, labContent, Encoding.UTF8, ct);
            }

            ConsoleServices.Output?.WriteLine($"Running MFA for speaker '{speaker}'...");
            var textGridPaths = await RunMfaOnSpeakerDirAsync(speakerDir, ct);

            foreach (var tgPath in textGridPaths)
            {
                var words = ParseTextGridSimple(tgPath);
                allMfaTimings.AddRange(words.Select(w => (w.Start, w.End)));
            }
        }

        ApplyTimeInterpolation(globalWords, allMfaTimings);

        foreach (var sentence in sentences)
        {
            if (sentence.Words.Count > 0)
            {
                sentence.Start = sentence.Words.Min(w => w.Start);
                sentence.End = sentence.Words.Max(w => w.End);
            }
        }

        return new SpeakerMfaResponse { Sentences = sentences };
    }

    // ---------- 辅助方法 ----------
    private List<MfaSegment> MergeToSegments(List<Sentence> sentences)
    {
        var segments = new List<MfaSegment>();
        var currentWords = new List<Word>();
        double currentStart = 0, currentEnd = 0;

        foreach (var sentence in sentences)
        {
            if (sentence.Words.Count == 0) continue;

            var sentenceStart = sentence.Words.First().Start;
            var sentenceEnd = sentence.Words.Last().End;
            var sentenceDuration = sentenceEnd - sentenceStart;

            if (currentWords.Count == 0)
            {
                currentWords.AddRange(sentence.Words);
                currentStart = sentenceStart;
                currentEnd = sentenceEnd;
                continue;
            }

            var newDuration = (currentEnd - currentStart) + sentenceDuration;
            if (newDuration <= MaxSegmentDurationSec)
            {
                currentWords.AddRange(sentence.Words);
                currentEnd = sentenceEnd;
            }
            else
            {
                if (currentEnd - currentStart >= MinSegmentDurationSec)
                {
                    segments.Add(new MfaSegment
                    {
                        StartMs = currentStart * 1000,
                        EndMs = currentEnd * 1000,
                        Words = new List<Word>(currentWords)
                    });
                }
                else
                {
                    // 强制合并
                    currentWords.AddRange(sentence.Words);
                    currentEnd = sentenceEnd;
                    segments.Add(new MfaSegment
                    {
                        StartMs = currentStart * 1000,
                        EndMs = currentEnd * 1000,
                        Words = new List<Word>(currentWords)
                    });
                    currentWords.Clear();
                    continue;
                }

                currentWords.Clear();
                currentWords.AddRange(sentence.Words);
                currentStart = sentenceStart;
                currentEnd = sentenceEnd;
            }
        }

        if (currentWords.Count > 0)
        {
            var duration = currentEnd - currentStart;
            if (duration < MinSegmentDurationSec && segments.Count > 0)
            {
                var last = segments[^1];
                if ((last.EndMs - last.StartMs) / 1000 + duration <= MaxSegmentDurationSec)
                {
                    last.Words.AddRange(currentWords);
                    last.EndMs = currentEnd * 1000;
                    return segments;
                }
            }

            segments.Add(new MfaSegment
            {
                StartMs = currentStart * 1000,
                EndMs = currentEnd * 1000,
                Words = new List<Word>(currentWords)
            });
        }

        return segments;
    }

    private async Task ExtractAudioSegmentWithSilenceAsync(
        string inputPath,
        double startMs,
        double endMs,
        string outputPath,
        CancellationToken ct)
    {
        var startSec = (startMs / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var endSec = (endMs / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var durationSec = (endMs - startMs) / 1000.0;
        var totalDurationWithPad = durationSec + (2.0 * SilencePaddingMs / 1000.0);

        var filterComplex =
            $"[0:a]adelay={SilencePaddingMs}|{SilencePaddingMs},apad=pad_dur={totalDurationWithPad:0.000}[a]";

        var args = $"-y -i \"{inputPath}\" -ss {startSec} -to {endSec} " +
                   $"-filter_complex \"{filterComplex}\" -map \"[a]\" " +
                   $"-ac 1 -ar 16000 -acodec pcm_s16le \"{outputPath}\"";

        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg", args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start FFmpeg.");

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"FFmpeg failed: {error}");
        }
    }

    private async Task<List<string>> RunMfaOnSpeakerDirAsync(string speakerDir, CancellationToken ct)
    {
        var outputDir = Path.Combine(speakerDir, "aligned");
        Directory.CreateDirectory(outputDir);

        var args = $"align \"{speakerDir}\" english_us_mfa english_mfa \"{outputDir}\" " +
                   $"--single_speaker --overwrite --cleanup";

        var output = await RunMfaCommandAsync(args, speakerDir, ct);
        return Directory.GetFiles(outputDir, "*.TextGrid").ToList();
    }

    private async Task<string> RunMfaCommandAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_mfaBinaryPath))
            throw new InvalidOperationException("MFA binary not found.");

        var psi = new System.Diagnostics.ProcessStartInfo(_mfaBinaryPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        if (!string.IsNullOrEmpty(_openFstBinPath))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = _openFstBinPath + Path.PathSeparator + currentPath;
        }
        psi.EnvironmentVariables["HF_ENDPOINT"] = "https://hf-mirror.com";

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start MFA process.");

        try
        {
            using var registration = ct.Register(() =>
            {
                if (!process.HasExited)
                    try { process.Kill(); } catch { }
            });

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                throw new Exception($"MFA command failed with exit code {process.ExitCode}. Error: {error}");
            }

            return await outputTask;
        }
        finally
        {
            // 不保留进程引用，因为这是同步调用
        }
    }

    private List<(string Text, double Start, double End)> ParseTextGridSimple(string path)
    {
        var content = File.ReadAllText(path);
        var results = new List<(string, double, double)>();

        var pattern = @"text\s*=\s*""([^""]*)""\s*xmin\s*=\s*([0-9.]+)\s*xmax\s*=\s*([0-9.]+)";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var text = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(text) && text != "<unk>" && text != "sp")
            {
                var start = double.Parse(match.Groups[2].Value);
                var end = double.Parse(match.Groups[3].Value);
                results.Add((text, start, end));
            }
        }

        return results;
    }

    private void ApplyTimeInterpolation(List<Word> newWords, List<(double Start, double End)> oldTimings)
    {
        if (oldTimings.Count == 0 || newWords.Count == 0)
            return;

        if (newWords.Count == oldTimings.Count)
        {
            for (int i = 0; i < newWords.Count; i++)
            {
                newWords[i].Start = oldTimings[i].Start;
                newWords[i].End = oldTimings[i].End;
            }
            return;
        }

        var oldBoundaries = new double[oldTimings.Count + 1];
        oldBoundaries[0] = oldTimings[0].Start;
        for (int i = 0; i < oldTimings.Count; i++)
            oldBoundaries[i + 1] = oldTimings[i].End;
        oldBoundaries[oldTimings.Count] = oldTimings[oldTimings.Count - 1].End;

        var totalDuration = oldBoundaries[^1] - oldBoundaries[0];
        if (totalDuration <= 0) return;

        var oldNorm = oldBoundaries.Select(b => (b - oldBoundaries[0]) / totalDuration).ToArray();

        var newNorm = new double[newWords.Count + 1];
        newNorm[0] = 0;
        newNorm[^1] = 1;
        for (int i = 1; i < newWords.Count; i++)
        {
            double t = (double)i / newWords.Count;
            newNorm[i] = Interpolate(oldNorm, t);
        }

        var timeStart = oldBoundaries[0];
        for (int i = 0; i < newWords.Count; i++)
        {
            newWords[i].Start = timeStart + newNorm[i] * totalDuration;
            newWords[i].End = timeStart + newNorm[i + 1] * totalDuration;
        }
    }

    private double Interpolate(double[] oldNorm, double t)
    {
        if (t <= 0) return oldNorm[0];
        if (t >= 1) return oldNorm[^1];

        int n = oldNorm.Length - 1;
        for (int i = 0; i < n; i++)
        {
            if (t >= oldNorm[i] && t <= oldNorm[i + 1])
            {
                double localT = (t - oldNorm[i]) / (oldNorm[i + 1] - oldNorm[i]);
                return oldNorm[i] + localT * (oldNorm[i + 1] - oldNorm[i]);
            }
        }
        return oldNorm[^1];
    }

    // ---------- 资源释放 ----------
    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Task.CompletedTask;
    }

    private class MfaSegment
    {
        public double StartMs { get; set; }
        public double EndMs { get; set; }
        public List<Word> Words { get; set; } = new();
    }
}