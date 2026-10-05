namespace Centurion.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when audio probing fails.
/// </summary>
public sealed class AudioProbeException(string message, Exception? inner = null) : Exception(message, inner);
