using Centurion.Abstractions;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Core.Operators.Request;
using Centurion.Core.Operators.Response;

namespace Centurion.Core.Operators;

/// <summary>
/// 字幕转换算子（如 SRT → ASS）
/// </summary>
public class SubtitleConverter : IOperator<SubtitleConvertRequest, SubtitleConvertResponse>, IAsyncDisposable
{
    private readonly IEnumerable<ISubtitleParser> _parsers;
    private readonly Dictionary<string, ISubtitleParser> _parserMap;
    private bool _disposed;

    /// <summary>
    /// 用一组按扩展名注册的字幕解析器初始化转换算子。
    /// </summary>
    /// <param name="parsers">支持不同字幕格式的解析器集合。</param>
    public SubtitleConverter(IEnumerable<ISubtitleParser> parsers)
    {
        _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
        _parserMap = _parsers
            .Where(p => !string.IsNullOrEmpty(p.SupportedExtension))
            .ToDictionary(p => p.SupportedExtension.ToLowerInvariant(), p => p, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 健康检查；本算子无外部依赖，始终直接返回成功。
    /// </summary>
    public Task CheckHealthAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据目标格式选择对应解析器读取字幕文件，并转换为 ASS 文档。
    /// </summary>
    /// <param name="request">包含字幕文件路径与目标格式的请求。</param>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    /// <returns>转换得到的 ASS 字幕文档。</returns>
    public async Task<SubtitleConvertResponse> ProcessAsync(
        OperatorsRequest<SubtitleConvertRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = request.Payload;

        var filePath = payload.FilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var extension = !string.IsNullOrEmpty(payload.Format) && payload.Format.StartsWith('.')
            ? payload.Format
            : "." + payload.Format;

        if (string.IsNullOrEmpty(extension))
            throw new ArgumentException("Cannot determine subtitle format.");

        if (!_parserMap.TryGetValue(extension, out var parser))
            throw new NotSupportedException($"Subtitle format '{extension}' is not supported.");

        var entries = await parser.ParseAsync(filePath, cancellationToken);
        if (entries == null || entries.Count == 0)
            throw new InvalidOperationException("No subtitle entries parsed.");

        var assDoc = BuildAssFromEntries(entries);
        return new SubtitleConvertResponse { Document = assDoc };
    }

    private AssSub BuildAssFromEntries(List<Sentence> sentences)
    {
        var builder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();
        foreach (var dialogue in sentences.Select(entry => new AssSubLineBuilder()
                     .WithComment(false)
                     .WithLayer(0)
                     .WithStart((long)entry.Start)
                     .WithEnd((long)entry.End)
                     .WithStyle("Default")
                     .WithName("")
                     .WithMarginL(0)
                     .WithMarginR(0)
                     .WithMarginV(0)
                     .WithEffect("")
                     .WithText(entry.Text)
                     .Build()))
            builder.Lines.Add(dialogue);
        return builder.Build();
    }

    // ---------- 资源释放 ----------
    /// <summary>
    /// 同步释放资源，内部转调 <see cref="DisposeAsync"/>。
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步释放资源，并释放实现了 <see cref="IDisposable"/> 的解析器。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 清理解析器资源（如果有）
        foreach (var parser in _parsers.OfType<IDisposable>())
            parser.Dispose();

        await Task.CompletedTask;
    }
}
