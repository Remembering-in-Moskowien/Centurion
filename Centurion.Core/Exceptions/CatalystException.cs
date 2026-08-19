namespace Centurion.Core.Exceptions;

public class CatalystException : Exception
{
    public CatalystException(string message) : base(message)
    {
    }

    public CatalystException(string message, Exception inner) : base(message, inner)
    {
    }
}