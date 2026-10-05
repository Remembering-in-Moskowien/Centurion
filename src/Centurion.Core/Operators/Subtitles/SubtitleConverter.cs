using Centurion.Abstractions;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Core.Operators.Subtitles.Request;
using Centurion.Core.Operators.Subtitles.Response;
namespace Centurion.Core.Operators.Subtitles;

/// <summary>
/// Subtitle conversion operator (e.g. SRT -> ASS).
/// </summary>
public class SubtitleConverter : IOperator<SubtitleConvertRequest, SubtitleConvertResponse>, IAsyncDisposable
{
    private readonly IEnumerable<ISubtitleParser> _parsers;
    private readonly Dictionary<string, ISubtitleParser> _parserMap;
    private bool _disposed;

    /// <summary>
    /// Initializes the conversion operator with a set of subtitle parsers registered by extension.
    /// </summary>
    /// <param name="parsers">A collection of parsers supporting different subtitle formats.</param>
    public SubtitleConverter(IEnumerable<ISubtitleParser> parsers)
    {
        _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
        _parserMap = _parsers
            .Where(p => !string.IsNullOrEmpty(p.SupportedExtension))
            .ToDictionary(p => p.SupportedExtension.ToLowerInvariant(), p => p, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Health check; this operator has no external dependencies and always returns success directly.
    /// </summary>
    public Task CheckHealthAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects the matching parser by target format, reads the subtitle file, and converts it to an ASS document.
    /// </summary>
    /// <param name="request">Request containing the subtitle file path and the target format.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The converted ASS subtitle document.</returns>
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

    // ---------- Resource cleanup ----------
    /// <summary>
    /// Synchronously releases resources, internally delegating to <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously releases resources, and disposes any parsers that implement <see cref="IDisposable"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Clean up parser resources (if any).
        foreach (var parser in _parsers.OfType<IDisposable>())
            parser.Dispose();

        await Task.CompletedTask;
    }
}
