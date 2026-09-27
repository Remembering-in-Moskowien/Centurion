namespace Centurion.Models.Workflow;

/// <summary>
/// 字幕生成工作流的全量上下文（Pipeline 唯一传递对象）
/// 设计为纯数据容器，可序列化以支持检查点/断点续传。
/// </summary>
public class SubtitleWorkflowContext(WorkflowConfig config)
{
    /// <summary>工作流配置（源自 CLI SubCommand；命令链中间可更新输出路径等字段）</summary>
    public WorkflowConfig Config { get; set; } = config;

    /// <summary>可变的工作流状态（由各 Operator 逐步填充）</summary>
    public WorkflowState State { get; set; } = new();
}
