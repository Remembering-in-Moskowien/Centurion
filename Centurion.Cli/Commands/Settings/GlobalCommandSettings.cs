using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// 所有命令共享的全局选项基类：
/// <list type="bullet">
/// <item><c>--json</c>：仅输出机器可读 JSON 摘要（抑制人类可读行，供脚本消费）；</item>
/// <item><c>--dry-run</c>：预览将执行的 DAG、模型与预计成本，不实际执行。</item>
/// </list>
/// 两个选项均可选，不影响旧命令用法（向后兼容）。
/// </summary>
public abstract class GlobalCommandSettings : CommandSettings
{
    /// <summary>以 JSON 输出命令结果摘要（脚本可消费；抑制普通控制台行）。</summary>
    [CommandOption("--json")]
    [Description("Output the result summary as machine-readable JSON (suppresses human lines)")]
    public bool Json { get; init; }

    /// <summary>预览将执行的 DAG 拓扑、涉及模型与预计成本，不执行任何算子。</summary>
    [CommandOption("--dry-run")]
    [Description("Preview DAG, models and estimated cost without executing")]
    public bool DryRun { get; init; }
}
