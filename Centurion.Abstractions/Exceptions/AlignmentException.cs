namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when alignment fails.
/// </summary>
public class AlignmentException : Exception
{
    public AlignmentException(string message) : base(message) { }
    public AlignmentException(string message, Exception inner) : base(message, inner) { }
}