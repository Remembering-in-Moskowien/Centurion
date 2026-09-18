using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// 管道算子基接口。
/// 遵循"数据驱动"原则：所有业务数据通过 SubtitleWorkflowContext 流转，
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
