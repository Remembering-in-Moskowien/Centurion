namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when the Whisper transcription process exits with a non-zero code.
/// </summary>
public class WhisperProcessException(string message, int exitCode, string errorLog) : Exception(message)
{
    /// <summary>
    /// The Whisper process exit code.
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// The error log output by Whisper.
    /// </summary>
    public string ErrorLog { get; } = errorLog;
}