namespace Centurion.Abstractions.Exceptions;

/// <summary>Aria2 process execution failed with a non-zero exit code.</summary>
public class AriaProcessExitException(string message, int exitCode) : Exception(message)
{
    /// <summary>
    /// The Aria2 process exit code.
    /// </summary>
    public int ExitCode { get; } = exitCode;
}

/// <summary>The file hash does not match the expected value.</summary>
public class FileHashMismatchException(string msg, string path, string expect, string actual) : Exception(msg)
{
    /// <summary>
    /// The path of the file that failed hash verification.
    /// </summary>
    public string FilePath { get; } = path;

    /// <summary>
    /// The expected file hash.
    /// </summary>
    public string ExpectHash { get; } = expect;

    /// <summary>
    /// The computed file hash.
    /// </summary>
    public string ActualHash { get; } = actual;
}