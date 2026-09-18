namespace Centurion.Abstractions.Pipeline;

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
