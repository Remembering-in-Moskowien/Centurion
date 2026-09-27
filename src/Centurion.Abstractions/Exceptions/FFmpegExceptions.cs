namespace Centurion.Abstractions.Exceptions;

/// <summary>FFmpeg 转换进程异常退出</summary>
public class FFmpegProcessExitException(string message, int exitCode, string errorLog) : Exception(message)
{
    /// <summary>
    /// FFmpeg 进程的退出码。
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// FFmpeg 输出的错误日志内容。
    /// </summary>
    public string ErrorLog { get; } = errorLog;
}