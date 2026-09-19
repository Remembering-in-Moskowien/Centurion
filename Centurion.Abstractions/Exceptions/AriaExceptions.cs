namespace Centurion.Abstractions.Exceptions;

/// <summary>Aria2 进程执行失败（非0退出码）</summary>
public class AriaProcessExitException(string message, int exitCode) : Exception(message)
{
    /// <summary>
    /// Aria2 进程的退出码。
    /// </summary>
    public int ExitCode { get; } = exitCode;
}

/// <summary>文件哈希校验不匹配</summary>
public class FileHashMismatchException(string msg, string path, string expect, string actual) : Exception(msg)
{
    /// <summary>
    /// 哈希校验不通过的文件路径。
    /// </summary>
    public string FilePath { get; } = path;

    /// <summary>
    /// 期望得到的文件哈希值。
    /// </summary>
    public string ExpectHash { get; } = expect;

    /// <summary>
    /// 实际计算得到的文件哈希值。
    /// </summary>
    public string ActualHash { get; } = actual;
}