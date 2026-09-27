namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Whisper 转录进程异常退出（非零退出码）时抛出的异常。
/// </summary>
public class WhisperProcessException(string message, int exitCode, string errorLog) : Exception(message)
{
    /// <summary>
    /// Whisper 进程的退出码。
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// Whisper 输出的错误日志内容。
    /// </summary>
    public string ErrorLog { get; } = errorLog;
}