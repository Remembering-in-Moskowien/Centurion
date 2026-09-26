using System.Text.Json;
using System.Text.Json.Serialization;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;

namespace Centurion.Models.Schema;

/// <summary>
/// IR（*.centurion.json）序列化的 System.Text.Json 源生成上下文：
/// 显式登记全部参与 IR 读写/校验的类型，避免运行时反射，
/// 并统一 camelCase 属性命名、缩进、忽略 null 与枚举字符串化。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CenturionDocument))]
[JsonSerializable(typeof(GeneratorInfo))]
[JsonSerializable(typeof(ProvenanceEntry))]
[JsonSerializable(typeof(WorkflowConfig))]
[JsonSerializable(typeof(AudioPreprocessConfig))]
[JsonSerializable(typeof(WorkflowState))]
[JsonSerializable(typeof(AudioProbeInfo))]
[JsonSerializable(typeof(Sentence))]
[JsonSerializable(typeof(Word))]
[JsonSerializable(typeof(DubSegment))]
[JsonSerializable(typeof(CorrectionReport))]
[JsonSerializable(typeof(SpellCheckIssue))]
[JsonSerializable(typeof(SubtitleTrackCheckResult))]
[JsonSerializable(typeof(MkvTrackInfo))]
[JsonSerializable(typeof(AssStyle))]
[JsonSerializable(typeof(List<CenturionDocument>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class CenturionJsonContext : JsonSerializerContext;
