namespace Centurion.Core.Exceptions;

/// <summary>Aria2 进程执行失败（非0退出码）</summary>
public class AriaProcessExitException(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>文件哈希校验不匹配</summary>
public class FileHashMismatchException(string msg, string path, string expect, string actual) : Exception(msg)
{
    public string FilePath { get; } = path;
    public string ExpectHash { get; } = expect;
    public string ActualHash { get; } = actual;
}