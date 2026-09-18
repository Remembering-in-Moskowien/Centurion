namespace Centurion.Abstractions.Exceptions;

public class WhisperProcessException(string message, int exitCode, string errorLog) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string ErrorLog { get; } = errorLog;
}