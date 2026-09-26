using Microsoft.Extensions.Logging;

namespace Centurion.Abstractions.Utils;

/// <summary>
/// 失败日志门控：保证一次进程运行内至多输出一次 "ERR"（LogError）级别日志，
/// 之后的所有失败一律降级为 "WARN"（LogWarning）输出。
/// <para>
/// 典型场景：管道中多个算子各自启动外部进程，逐个失败时不再刷屏多条 fail，
/// 只保留第一条作为运行失败的权威信号，其余以警告呈现，便于阅读与日志轮转。
/// 使用 <see cref="Interlocked"/> 保证并发安全；单次 CLI 运行内为进程级单例状态。
/// </para>
/// </summary>
public static class FailLogGate
{
    private static int _failed;

    /// <summary>本次运行是否已经输出过一次 fail。</summary>
    public static bool HasFailed => Volatile.Read(ref _failed) != 0;

    /// <summary>
    /// 记录失败：首次调用输出 ERR，其后降级为 WARN。logger 为 null 时静默跳过。
    /// </summary>
    /// <param name="logger">目标日志器；可为空。</param>
    /// <param name="message">日志消息（可含占位符）。</param>
    /// <param name="args">占位符参数。</param>
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
    /// 记录带异常上下文的失败：首次调用输出 ERR，其后降级为 WARN。logger 为 null 时静默跳过。
    /// </summary>
    /// <param name="logger">目标日志器；可为空。</param>
    /// <param name="exception">关联的异常。</param>
    /// <param name="message">日志消息（可含占位符）。</param>
    /// <param name="args">占位符参数。</param>
    public static void Log(ILogger? logger, Exception exception, string message, params object?[] args)
    {
        if (logger is null)
            return;

        if (Interlocked.CompareExchange(ref _failed, 1, 0) == 0)
            logger.LogError(exception, message, args);
        else
            logger.LogWarning(exception, message, args);
    }

    /// <summary>重置门控状态（仅供测试与需要重开计数场景使用）。</summary>
    public static void Reset() => Interlocked.Exchange(ref _failed, 0);
}
