using Centurion.Core.Models;

namespace Centurion.Core.Abstractions;

/// <summary>
/// 管道算子基接口。
/// 遵循“数据驱动”原则：所有业务数据通过 SubtitleWorkflowContext 流转，
/// 算子只负责对上下文进行变换（Transform）。
/// </summary>
public interface IPipelineOperator
{
    /// <summary>
    /// 算子名称（用于日志和进度展示）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行管道变换。
    /// 算子内部从 context.Config 读取配置，从 context.State 读取输入，
    /// 并将处理结果写回 context.State。
    /// </summary>
    /// <param name="context">全量工作流上下文（引用传递）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 支持健康检查的管道算子（可选实现）。
/// 用于需要校验外部依赖（如模型文件是否存在、ffmpeg是否安装）的算子。
/// </summary>
public interface IHealthCheckableOperator : IPipelineOperator
{
    /// <summary>
    /// 校验算子运行环境是否就绪。
    /// </summary>
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 支持进度报告的管道算子（可选实现）。
/// 用于长耗时任务，向控制台/UI 反馈执行进度。
/// </summary>
public interface IProgressReportableOperator : IPipelineOperator
{
    /// <summary>
    /// 进度事件。订阅方（如 CLI SubCommand）可绑定此事件以渲染进度条。
    /// </summary>
    event EventHandler<OperatorProgressEventArgs>? Progress;
}

/// <summary>
/// 算子进度事件参数
/// </summary>
public class OperatorProgressEventArgs : EventArgs
{
    public string OperatorName { get; init; } = string.Empty;
    public int Percentage { get; init; } // 0-100
    public string? StatusMessage { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}