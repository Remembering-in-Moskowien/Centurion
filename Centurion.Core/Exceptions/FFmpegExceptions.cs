namespace Centurion.Core.Exceptions;

/// <summary>FFmpeg 转换进程异常退出</summary>
public class FFmpegProcessExitException(string message, int exitCode, string errorLog) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string ErrorLog { get; } = errorLog;
}