using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Centurion.Core.Response;

namespace Centurion.Core.Operators;

/// <summary>
/// 字幕转换算子（如 SRT → ASS）
/// </summary>
public class SubtitleConverterOperator : IOperator<SubtitleConvertRequest, SubtitleConvertResponse>, IAsyncDisposable
{
    private readonly IEnumerable<ISubtitleParser> _parsers;
    private readonly Dictionary<string, ISubtitleParser> _parserMap;
    private bool _disposed;

    public SubtitleConverterOperator(IEnumerable<ISubtitleParser> parsers)
    {
        _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
        _parserMap = _parsers
            .Where(p => !string.IsNullOrEmpty(p.SupportedExtension))
            .ToDictionary(p => p.SupportedExtension.ToLowerInvariant(), p => p, StringComparer.OrdinalIgnoreCase);
    }

    public Task EnsureTargetAvailableAsync() => Task.CompletedTask;

    public async Task<SubtitleConvertResponse> ProcessAsync(
        OperatorsRequest<SubtitleConvertRequest> request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var payload = request.Payload;

        var filePath = payload.FilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var extension = !string.IsNullOrEmpty(payload.Format) && payload.Format.StartsWith('.')
            ? payload.Format
            : "." + payload.Format ?? Path.GetExtension(filePath);

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

    private AssSub BuildAssFromEntries(List<SubtitleEntry> entries)
    {
        var builder = new AssSubBuilder().WithDefaultValues().WithAddDefaultStyle();
        foreach (var entry in entries)
        {
            var dialogue = new AssSubLineBuilder()
                .WithComment(false)
                .WithLayer(0)
                .WithStart(entry.StartMs)
                .WithEnd(entry.EndMs)
                .WithStyle("Default")
                .WithName("")
                .WithMarginL(0)
                .WithMarginR(0)
                .WithMarginV(0)
                .WithEffect("")
                .WithText(entry.Text)
                .Build();
            builder.Lines.Add(dialogue);
        }
        return builder.Build();
    }

    // ---------- 资源释放 ----------
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

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