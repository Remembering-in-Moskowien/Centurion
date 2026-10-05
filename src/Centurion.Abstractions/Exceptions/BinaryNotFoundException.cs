namespace Centurion.Abstractions.Exceptions;

/// <summary>Exception thrown when an external executable cannot be found.</summary>
public class BinaryNotFoundException(string message, string binaryName) : Exception(message)
{
    /// <summary>
    /// The name of the missing external executable, such as ffmpeg.exe.
    /// </summary>
    public string BinaryName { get; } = binaryName;
}