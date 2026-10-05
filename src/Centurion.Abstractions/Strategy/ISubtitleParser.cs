using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Subtitle parser interface; each subtitle format provides an implementation.
/// </summary>
public interface ISubtitleParser
{
    /// <summary>Supported extension, such as ".srt".</summary>
    string SupportedExtension { get; }

    /// <summary>Parses a file into a list of subtitle entries.</summary>
    Task<List<Sentence>> ParseAsync(string filePath, CancellationToken ct = default);
}