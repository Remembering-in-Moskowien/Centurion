using Centurion.Models.Workflow;

namespace Centurion.Models.Schema;

/// <summary>
/// IR（中间文件 *.centurion.json）格式版本常量与支持矩阵。
/// IR 是命令链中唯一的结构化交换契约：schemaVersion 标识结构版本，
/// 旧版本文件可通过 `migrate` 命令显式升级到当前版本。
/// </summary>
public static class CenturionSchema
{
    /// <summary>工具名（写入 generator.tool）。</summary>
    public const string ToolName = "centurion";

    /// <summary>当前 IR 结构版本（结构变更时递增；历史版本进入迁移链）。</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>旧版中间文件的 meta.version 数值（CenturionFileIO v1，仅 meta/config/state 三字段）。</summary>
    public const int LegacyMetaVersion = 1;

    /// <summary>判断版本字符串是否为当前受支持版本。</summary>
    public static bool IsSupported(string? version) =>
        string.Equals(version, CurrentVersion, StringComparison.Ordinal);
}

/// <summary>
/// IR 根对象：schemaVersion + generator（生成工具信息）+ provenance（处理溯源）
/// + config（不可变工作流配置）+ state（可变工作流状态）。
/// 所有 *.centurion.json 的读写都以本对象为唯一契约。
/// </summary>
public sealed class CenturionDocument
{
    /// <summary>IR 结构版本（如 "1.0"），读取时用于兼容性校验。</summary>
    public string SchemaVersion { get; set; } = CenturionSchema.CurrentVersion;

    /// <summary>生成本文件的工具信息（版本、命令、时间、输入输出）。</summary>
    public GeneratorInfo Generator { get; set; } = new();

    /// <summary>
    /// 处理溯源：本文件生命周期内已执行的处理步骤（operator + 模型 + 配置指纹）。
    /// 为空表示尚未执行任何算子（如 from-script / convert 产物）。
    /// </summary>
    public List<ProvenanceEntry> Provenance { get; set; } = [];

    /// <summary>不可变工作流配置（与旧版 config 相同）。</summary>
    public WorkflowConfig Config { get; set; } = new();

    /// <summary>可变工作流状态（与旧版 state 相同；Extensions 为进程内临时数据，不持久化）。</summary>
    public WorkflowState State { get; set; } = new();
}

/// <summary>生成工具信息：谁、用什么版本、在哪条命令、什么时间生成了本文件。</summary>
public sealed class GeneratorInfo
{
    /// <summary>工具名（恒为 "centurion"）。</summary>
    public string Tool { get; set; } = CenturionSchema.ToolName;

    /// <summary>生成时的工具版本（程序集 InformationalVersion）。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>触发本次保存的子命令名（如 "asr"、"dub"）。</summary>
    public string? Command { get; set; }

    /// <summary>生成时间（ISO 8601，UTC+本地偏移）。</summary>
    public string GeneratedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    /// <summary>本次处理的输入文件路径。</summary>
    public string? InputFile { get; set; }

    /// <summary>本中间文件输出路径。</summary>
    public string? OutputFile { get; set; }
}

/// <summary>
/// 单条处理溯源记录：某个处理步骤由哪个 operator 执行、使用什么模型、
/// 以及基于什么配置（参数指纹）。
/// </summary>
public sealed class ProvenanceEntry
{
    /// <summary>执行本步骤的算子/命令名（如 "transcribe"、"spellcheck"）。</summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>本步骤使用的模型名（未使用模型时为 null，如音频转换）。</summary>
    public string? Model { get; set; }

    /// <summary>
    /// 参数指纹：本步骤基于的工作流配置的 SHA-256 哈希（十六进制）。
    /// 配置一致则指纹一致，用于追溯"这份结果是用什么参数生成的"。
    /// </summary>
    public string? ParametersHash { get; set; }

    /// <summary>本步骤完成时间（ISO 8601）。</summary>
    public string? AppliedAt { get; set; }
}
