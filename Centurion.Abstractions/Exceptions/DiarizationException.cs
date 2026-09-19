namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// 自定义异常
/// </summary>
public class DiarizationException : Exception
{
    /// <summary>
    /// 使用指定错误消息初始化异常实例。
    /// </summary>
    /// <param name="message">描述错误原因的消息。</param>
    public DiarizationException(string message) : base(message)
    {
    }

    /// <summary>
    /// 使用指定错误消息和内部异常初始化异常实例。
    /// </summary>
    /// <param name="message">描述错误原因的消息。</param>
    /// <param name="inner">导致当前异常的内部异常。</param>
    public DiarizationException(string message, Exception inner) : base(message, inner)
    {
    }
}