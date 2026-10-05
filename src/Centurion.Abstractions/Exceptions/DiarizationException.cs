namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when diarization fails.
/// </summary>
public class DiarizationException : Exception
{
    /// <summary>
    /// Initializes an exception with the specified error message.
    /// </summary>
    /// <param name="message">A message describing the cause of the error.</param>
    public DiarizationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes an exception with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the cause of the error.</param>
    /// <param name="inner">The exception that caused the current exception.</param>
    public DiarizationException(string message, Exception inner) : base(message, inner)
    {
    }
}