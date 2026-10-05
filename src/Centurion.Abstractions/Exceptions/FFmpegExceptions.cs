namespace Centurion.Abstractions.Exceptions;

/// <summary>FFmpeg conversion process exited with an error.</summary>
public class FFmpegProcessExitException(string message, int exitCode, string errorLog) : Exception(message)
{
    /// <summary>
    /// The FFmpeg process exit code.
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// The error log output by FFmpeg.
    /// </summary>
    public string ErrorLog { get; } = errorLog;
}