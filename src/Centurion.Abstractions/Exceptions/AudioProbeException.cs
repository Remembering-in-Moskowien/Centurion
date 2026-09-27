namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// 音频探测失败异常
/// </summary>
public sealed class AudioProbeException(string message, Exception? inner = null) : Exception(message, inner);
