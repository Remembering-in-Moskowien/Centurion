namespace Centurion.Core.Exceptions;

/// <summary>
/// 自定义异常
/// </summary>
public class DiarizationException : Exception
{
    public DiarizationException(string message) : base(message) { }
    public DiarizationException(string message, Exception inner) : base(message, inner) { }
}