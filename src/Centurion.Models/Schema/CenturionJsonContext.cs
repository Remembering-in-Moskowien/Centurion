using System.Text.Json;
using System.Text.Json.Serialization;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;

namespace Centurion.Models.Schema;

/// <summary>
/// System.Text.Json source-generation context for IR (*.centurion.json) serialization:
/// explicitly registers all types involved in IR read/write/validation to avoid runtime reflection,
/// and unifies camelCase property naming, indentation, null ignoring, and string-enum conversion.
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
