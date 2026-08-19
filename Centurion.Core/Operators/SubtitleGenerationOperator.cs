using System.Text;
using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Metadata;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Centurion.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Operators;

/// <summary>
/// 字幕生成算子：从媒体文件生成 ASS 字幕。
/// 流程：FFmpeg转换 → Whisper转录（使用 FasterWhisper.NET 纯 .NET 实现）→ Catalyst分句 → 说话人分割 →
///       (可选) 说话人感知MFA对齐 → ASS构建。
/// </summary>
public class SubtitleGeneratorOperator(
    CatalystSplitOperator catalystOperator,
    FFmpegConvertOperator ffmpegConvert,
    IServiceProvider serviceProvider,
    DiarizationOperator diarizationOperator,
    SpeakerMfaAlignmentOperator speakerMfaAlignment,
    ITempDirectoryManager tempManager)
    : IOperator<MediaGenerationRequest, SubtitleGenerationResponse>, IAsyncDisposable
{
    private readonly CatalystSplitOperator _catalystOperator = catalystOperator ?? throw new ArgumentNullException(nameof(catalystOperator));
    private readonly FFmpegConvertOperator _ffmpegConvert = ffmpegConvert ?? throw new ArgumentNullException(nameof(ffmpegConvert));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly DiarizationOperator _diarizationOperator = diarizationOperator ?? throw new ArgumentNullException(nameof(diarizationOperator));
    private readonly SpeakerMfaAlignmentOperator _speakerMfaAlignment = speakerMfaAlignment ?? throw new ArgumentNullException(nameof(speakerMfaAlignment));
    private readonly ITempDirectoryManager _tempManager = tempManager ?? throw new ArgumentNullException(nameof(tempManager));

    private bool _disposed;

    public async Task EnsureTargetAvailableAsync() => await Task.CompletedTask;

    public async Task<SubtitleGenerationResponse> ProcessAsync(
        OperatorsRequest<MediaGenerationRequest> request,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload;
        return await GenerateSubtitleAsync(payload, cancellationToken);
    }

    private async Task<SubtitleGenerationResponse> GenerateSubtitleAsync(
        MediaGenerationRequest payload,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var audioPath = payload.InputFilePath;
        ConsoleServices.Output?.WriteLine($"Processing media: {audioPath}, model: {payload.ModelName}, language: {payload.Language}");

        await using var tempDir = await _tempManager.CreateTempDirectoryAsync("subgen_");
        var taskTempDir = tempDir.Path;
        var tempWav = Path.Combine(taskTempDir, "audio.wav");

        // 1. 转换音频
        var convertRequest = new OperatorsRequest<FFmpegConvertRequest>
        {
            Payload = new FFmpegConvertRequest { InputFilePath = audioPath, OutputFilePath = tempWav }
        };
        await _ffmpegConvert.ProcessAsync(convertRequest, ct);

        // 2. Whisper 转录（使用 FasterWhisper.NET 手动下载版）
        ConsoleServices.Output?.WriteLine("Whisper transcription started (FasterWhisper.NET local)");

        // 使用 FasterWhisperModels 字典，分类文件夹为 "fasterwhisper"
        var modelManager = ActivatorUtilities.CreateInstance<ModelManager>(
            _serviceProvider, payload.ModelName, ModelRegistry.FasterWhisperModels, "fasterwhisper");
        await modelManager.EnsureModelAvailableAsync();

        // 获取模型目录（ModelFilePath 在目录模式下就是目录路径）
        var modelDir = modelManager.ModelFilePath;
        using var whisperOp = new WhisperFasterOperator(modelDir, device: "cpu", computeType: "int8");

        var whisperRequest = new OperatorsRequest<WhisperTranscribeRequest>
        {
            Payload = new WhisperTranscribeRequest
            {
                FilePath = tempWav,
                Language = payload.Language,
                InitialPrompt = payload.InitialPrompt
            }
        };
        var whisperResult = await whisperOp.ProcessAsync(whisperRequest, ct);
        var sentenceRaw = whisperResult.Sentence;

        if (sentenceRaw == null || sentenceRaw.Words.Count == 0)
            throw new InvalidOperationException("Whisper returned no words");

        sentenceRaw.Words = sentenceRaw.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (sentenceRaw.Words.Count == 0)
            throw new InvalidOperationException("No valid word timings after filtering");

        ConsoleServices.Output?.WriteLine($"Whisper transcription done, {sentenceRaw.Words.Count} words");

        // 3. Catalyst 分句
        ConsoleServices.Output?.WriteLine($"Catalyst split started, max_len: {payload.MaxLength}, target_len: {payload.TargetLength}");
        var splitRequest = new OperatorsRequest<SentenceSplitRequest>
        {
            Payload = new SentenceSplitRequest
            {
                Sentence = sentenceRaw,
                Language = payload.Language,
                MaxLength = payload.MaxLength,
                TargetLength = payload.TargetLength,
                SpreadRange = payload.SpreadRange
            }
        };
        var sentences = await _catalystOperator.ProcessAsync(splitRequest, ct);
        if (sentences == null || sentences.Count == 0)
        {
            ConsoleServices.Output?.WriteError("Catalyst split returned empty result");
            throw new InvalidOperationException("Catalyst split returned empty result");
        }
        ConsoleServices.Output?.WriteLine($"Catalyst split done, {sentences.Count} sentences");

        // 4. 说话人分割
        ConsoleServices.Output?.WriteLine("Starting speaker diarization...");
        var diarizationRequest = new OperatorsRequest<DiarizationRequest>
        {
            Payload = new DiarizationRequest { AudioFilePath = tempWav, Sentences = sentences }
        };
        var diarizationResult = await _diarizationOperator.ProcessAsync(diarizationRequest, ct);
        sentences = diarizationResult.Sentences;

        // 5. MFA 对齐（若启用）
        if (payload.UseMfa)
        {
            ConsoleServices.Output?.WriteLine("Starting speaker-aware MFA alignment...");
            var mfaRequest = new OperatorsRequest<SpeakerMfaRequest>
            {
                Payload = new SpeakerMfaRequest { AudioFilePath = tempWav, Sentences = sentences }
            };
            var mfaResult = await _speakerMfaAlignment.ProcessAsync(mfaRequest, ct);
            sentences = mfaResult.Sentences;
        }

        // 6. 构建 ASS 字幕
        ConsoleServices.Output?.WriteLine("Building ASS subtitles...");
        var subBuilder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();

        foreach (var group in sentences)
        {
            var startMs = (long)group.Start;
            var endMs = (long)group.End;
            var text = payload.Karaoke ? BuildKaraokeFromWords(group) : SubTools.NormalizeSpaces(group.Text);

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

        ConsoleServices.Output?.WriteLine($"ASS subtitles built, {subBuilder.Lines.Count} lines");
        return new SubtitleGenerationResponse { Document = subBuilder.Build() };
    }

    private static string BuildKaraokeFromWords(Sentence group)
    {
        var words = group.Words;
        if (words == null || words.Count == 0) return string.Empty;

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
            if (i < words.Count - 1) sb.Append(' ');
        }
        return sb.ToString();
    }

    // ---------- 资源释放 ----------
    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Task.CompletedTask;
    }
}