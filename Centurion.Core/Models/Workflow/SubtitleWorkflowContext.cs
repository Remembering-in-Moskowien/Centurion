namespace Centurion.Core.Models.Workflow;

/// <summary>
/// 字幕生成工作流的全量上下文（Pipeline 唯一传递对象）
/// 设计为纯数据容器，可序列化以支持检查点/断点续传。
/// </summary>
public class SubtitleWorkflowContext(WorkflowConfig config)
{
    /// <summary>不可变的用户配置（源自 CLI SubCommand）</summary>
    public WorkflowConfig Config { get; init; } = config;

    /// <summary>可变的工作流状态（由各 Operator 逐步填充）</summary>
    public WorkflowState State { get; set; } = new();
}
