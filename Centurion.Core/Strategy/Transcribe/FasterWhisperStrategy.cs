using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Qourex.FasterWhisper.NET;

namespace Centurion.Core.Strategy.Transcribe;

/// <summary>
/// 基于本地 FasterWhisper.NET 的转录策略
/// </summary>
public class FasterWhisperStrategy : ITranscriptionStrategy
{
    private readonly IModelPathResolver _pathResolver;
    private readonly string _device;
    private readonly string _computeType;

    public string StrategyName => "FasterWhisper.NET (Local)";

    public FasterWhisperStrategy(
        IModelPathResolver pathResolver,
        string device = "cpu",
        string computeType = "int8")
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _device = device;
        _computeType = computeType;
    }

    public async Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 确保模型目录存在并获取路径
        var modelPath = await _pathResolver.GetWhisperModelPathAsync(modelName, cancellationToken);

        // 2. 加载模型
        using var model = await Task.Run(
            () => new WhisperModel(modelPath, _device, _computeType),
            cancellationToken);

        // 3. 配置选项
        var options = new WhisperOptions
        {
            WordTimestamps = true,
            MedianFilterWidth = 7,
            InitialPrompt = initialPrompt,
            BeamSize = 1, // 贪心解码，时间戳更稳定
            ReturnScores = true,
            ReturnNoSpeechProb = true,
            ConditionOnPreviousText = true
        };

        // 4. 执行转录
        var segments = await Task.Run(
            () => model.Transcribe(audioPath, language, options: options),
            cancellationToken);

        // 5. 解析词级时间戳并转换为统一格式
        var words = segments
            .SelectMany(seg => seg.Words ?? Enumerable.Empty<WhisperWord>())
            .Where(w => !string.IsNullOrWhiteSpace(w.Word) && !IsSpecialToken(w.Word))
            .Select(w => new Word
            {
                Text = w.Word,
                Start = w.Start * 1000, // 转换为毫秒
                End = w.End * 1000,
                Speaker = "UNKNOWN" // 后续由说话人分割填充
            })
            .ToList();

        if (words.Count == 0)
            throw new InvalidOperationException("No valid words extracted.");

        return words;
    }

    private static bool IsSpecialToken(string text)
    {
        return text.StartsWith("[_") && text.EndsWith("]") &&
               (text.Contains("_BEG_") || text.Contains("_TT_") ||
                text.Contains("_EOT_") || text.Contains("_SOT_"));
    }
}