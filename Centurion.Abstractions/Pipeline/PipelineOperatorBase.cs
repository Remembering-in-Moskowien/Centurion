using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;
using Centurion.Models.Console;
namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// 管道算子抽象基类。
/// 提供名称、日志、进度事件及可选的健康检查虚方法。
/// 子类只需重写 ExecuteAsync 核心业务逻辑。
/// </summary>
public abstract class PipelineOperatorBase<TLogger> : IPipelineOperator, IProgressReportableOperator
    where TLogger : class
{
    /// <summary>
    /// 使用指定的日志记录器初始化管道算子基类。
    /// </summary>
    /// <param name="logger">用于记录本算子日志的日志记录器。</param>
    protected PipelineOperatorBase(ILogger<TLogger> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 派生类可用的日志记录器，日志前缀会自动带上算子名称。
    /// </summary>
    protected ILogger<TLogger> Logger { get; }

    // ---------- 核心抽象 ----------
    /// <summary>
    /// 算子名称（用于日志和进度展示）。
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 执行管道变换：从工作流上下文读取输入并写回处理结果。
    /// </summary>
    /// <param name="context">全量工作流上下文（引用传递）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public abstract Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken);

    // ---------- 进度事件（由调用方订阅） ----------
    /// <summary>
    /// 进度报告事件，调用方可订阅以渲染进度。
    /// </summary>
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

    /// <summary>
    /// 记录一条信息级日志，自动附带算子名称前缀。
    /// </summary>
    /// <param name="message">日志消息正文。</param>
    protected void LogInfo(string message)
    {
        Logger.LogInformation("[{Operator}] {Message}", Name, message);
    }

    /// <summary>
    /// 记录一条警告级日志，自动附带算子名称前缀。
    /// 算子内失败属内部细节：一律以 warn 记录并继续，由最外层统一输出一次 fail。
    /// </summary>
    /// <param name="message">日志消息正文。</param>
    protected void LogWarning(string message)
    {
        Logger.LogWarning("[{Operator}] {Message}", Name, message);
    }

    // ---------- 健康检查（可选重写） ----------
    /// <summary>
    /// 校验算子运行环境是否就绪；默认实现直接视为就绪，子类可按需重写。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
