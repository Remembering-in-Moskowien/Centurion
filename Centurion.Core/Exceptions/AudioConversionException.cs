namespace Centurion.Core.Exceptions;

/// <summary>
/// Exception thrown when audio conversion via FFmpeg fails.
/// </summary>
public class AudioConversionException : Exception
{
    public AudioConversionException(string message) : base(message)
    {
    }

    public AudioConversionException(string message, Exception inner) : base(message, inner)
    {
    }
}
