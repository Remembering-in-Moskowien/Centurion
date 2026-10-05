namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when audio conversion via FFmpeg fails.
/// </summary>
public class AudioConversionException : Exception
{
    /// <summary>
    /// Initializes an exception with the specified error message.
    /// </summary>
    /// <param name="message">A message describing the cause of the error.</param>
    public AudioConversionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes an exception with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the cause of the error.</param>
    /// <param name="inner">The exception that caused the current exception.</param>
    public AudioConversionException(string message, Exception inner) : base(message, inner)
    {
    }
}
