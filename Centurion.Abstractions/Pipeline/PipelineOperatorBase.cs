using Centurion.Models.Console;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// 管道算子抽象基类。
/// 提供名称、日志、进度事件及可选的健康检查虚方法。
/// 子类只需重写 ExecuteAsync 核心业务逻辑。
/// </summary>
public abstract class PipelineOperatorBase<TLogger> : IPipelineOperator, IProgressReportableOperator
    where TLogger : class
{
    protected PipelineOperatorBase(ILogger<TLogger> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected ILogger<TLogger> Logger { get; }

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

    protected void LogInfo(string message)
    {
        Logger.LogInformation("[{Operator}] {Message}", Name, message);
    }

    protected void LogWarning(string message)
    {
        Logger.LogWarning("[{Operator}] {Message}", Name, message);
    }

    protected void LogError(string message)
    {
        Logger.LogError("[{Operator}] {Message}", Name, message);
    }

    // ---------- 健康检查（可选重写） ----------
    public virtual Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
