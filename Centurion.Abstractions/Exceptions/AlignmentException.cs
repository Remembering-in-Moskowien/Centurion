namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when alignment fails.
/// </summary>
public class AlignmentException : Exception
{
    /// <summary>
    /// 使用指定错误消息初始化异常实例。
    /// </summary>
    /// <param name="message">描述错误原因的消息。</param>
    public AlignmentException(string message) : base(message) { }

    /// <summary>
    /// 使用指定错误消息和内部异常初始化异常实例。
    /// </summary>
    /// <param name="message">描述错误原因的消息。</param>
    /// <param name="inner">导致当前异常的内部异常。</param>
    public AlignmentException(string message, Exception inner) : base(message, inner) { }
}