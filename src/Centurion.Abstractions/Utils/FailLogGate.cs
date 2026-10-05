using Microsoft.Extensions.Logging;

namespace Centurion.Abstractions.Utils;

/// <summary>
/// Failure log gate that emits at most one "ERR" (LogError) entry per process run,
/// then downgrades subsequent failures to "WARN" (LogWarning).
/// <para>
/// This is useful when multiple pipeline operators launch external processes: keep the first failure as the authoritative error
/// and report later failures as warnings to avoid noisy output and simplify log rotation.
/// <see cref="Interlocked"/> provides thread safety; the state is process-wide for one CLI run.
/// </para>
/// </summary>
public static class FailLogGate
{
    private static int _failed;

    /// <summary>Whether a failure has already been logged during this run.</summary>
    public static bool HasFailed => Volatile.Read(ref _failed) != 0;

    /// <summary>
    /// Logs a failure as ERR on the first call and WARN on subsequent calls. Does nothing when logger is null.
    /// </summary>
    /// <param name="logger">Target logger; may be null.</param>
    /// <param name="message">Log message, optionally containing placeholders.</param>
    /// <param name="args">Placeholder values.</param>
    public static void Log(ILogger? logger, string message, params object?[] args)
    {
        if (logger is null)
            return;

        if (Interlocked.CompareExchange(ref _failed, 1, 0) == 0)
            logger.LogError(message, args);
        else
            logger.LogWarning(message, args);
    }

    /// <summary>
    /// Logs a failure with exception context as ERR on the first call and WARN on subsequent calls. Does nothing when logger is null.
    /// </summary>
    /// <param name="logger">Target logger; may be null.</param>
    /// <param name="exception">Related exception.</param>
    /// <param name="message">Log message, optionally containing placeholders.</param>
    /// <param name="args">Placeholder values.</param>
    public static void Log(ILogger? logger, Exception exception, string message, params object?[] args)
    {
        if (logger is null)
            return;

        if (Interlocked.CompareExchange(ref _failed, 1, 0) == 0)
            logger.LogError(exception, message, args);
        else
            logger.LogWarning(exception, message, args);
    }

    /// <summary>Resets the gate state for tests or scenarios that need to restart the count.</summary>
    public static void Reset() => Interlocked.Exchange(ref _failed, 0);
}
