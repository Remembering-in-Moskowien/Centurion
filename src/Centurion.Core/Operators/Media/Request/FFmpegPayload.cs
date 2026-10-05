namespace Centurion.Core.Operators.Media.Request;

/// <summary>FFmpeg conversion request payload, replacing the previous static-method parameters.</summary>
public class FFmpegConvertRequest
{
    /// <summary>Input audio path.</summary>
    public string InputFilePath { get; init; } = string.Empty;

    /// <summary>Required: output file path.</summary>
    public string? OutputFilePath { get; init; }
}

/// <summary>FFmpeg audio split request payload describing the input file to cut and its segment information.</summary>
public class FFmpegSplitRequest
{
    /// <summary>Path of the input audio file to split.</summary>
    public required string InputFilePath { get; init; }
    /// <summary>List of (start, end) times in milliseconds for each segment.</summary>
    public required List<(long StartMs, long EndMs)> Segments { get; init; }
    /// <summary>Optional output directory; used to generate output files when OutputFileNames is not provided.</summary>
    public string? OutputDirectory { get; set; } // Optional; used when OutputFileNames is empty.
    /// <summary>Optional list of output file names; if provided, its length must match Segments.</summary>
    public List<string>? OutputFileNames { get; set; } // Optional; if provided, its length must match Segments.
}