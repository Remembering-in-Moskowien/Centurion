using Centurion.Core.Models;

namespace Centurion.Core.Abstractions;

/// <summary>
/// 管道算子抽象基类。
/// 提供名称、日志、进度事件及可选的健康检查虚方法。
/// 子类只需重写 ExecuteAsync 核心业务逻辑。
/// </summary>
public abstract class PipelineOperatorBase : IPipelineOperator, IProgressReportableOperator
{
    // ---------- 核心抽象 ----------
    public abstract string Name { get; }
    public abstract Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken);

    // ---------- 进度事件（由调用方订阅） ----------
    public event EventHandler<OperatorProgressEventArgs>? Progress;

    /// <summary>
    /// 触发进度事件。子类在适当位置调用此方法报告进度。
    /// </summary>
    protected virtual void OnProgress(int percentage, string? message = null)
    {
        Progress?.Invoke(this, new OperatorProgressEventArgs
        {
            OperatorName = Name,
            Percentage = Math.Clamp(percentage, 0, 100),
            StatusMessage = message
        });
        // 可选：同时输出到控制台（便于调试）
        // ConsoleServices.Output?.WriteLine($"[{Name}] {percentage}% - {message}");
    }

    // ---------- 日志辅助（利用现有的 ConsoleServices） ----------
    protected void LogInfo(string message)
    {
        ConsoleServices.Output.WriteLine($"[{Name}] {message}");
    }

    protected void LogWarning(string message)
    {
        ConsoleServices.Output.WriteWarning($"[{Name}] {message}");
    }

    protected void LogError(string message)
    {
        ConsoleServices.Output.WriteError($"[{Name}] {message}");
    }

    // ---------- 健康检查（可选重写） ----------
    public virtual Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}