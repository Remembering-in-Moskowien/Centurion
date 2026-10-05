namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when alignment fails.
/// </summary>
public class AlignmentException : Exception
{
    /// <summary>
    /// Initializes an exception with the specified error message.
    /// </summary>
    /// <param name="message">A message describing the cause of the error.</param>
    public AlignmentException(string message) : base(message) { }

    /// <summary>
    /// Initializes an exception with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the cause of the error.</param>
    /// <param name="inner">The exception that caused the current exception.</param>
    public AlignmentException(string message, Exception inner) : base(message, inner) { }
}