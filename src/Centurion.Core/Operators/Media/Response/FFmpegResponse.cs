namespace Centurion.Core.Operators.Media.Response;

/// <summary>Result returned by the file-output conversion mode.</summary>
public class FFmpegConvertResponse
{
    /// <summary>Path of the output file after conversion.</summary>
    public string OutputPath { get; set; } = string.Empty;
}

/// <summary>Result returned by the FFmpeg audio split operation.</summary>
public class FFmpegSplitResponse
{
    /// <summary>List of output file paths for each segment.</summary>
    public List<string> OutputFiles { get; set; } = [];
}