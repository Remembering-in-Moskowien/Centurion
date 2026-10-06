using Centurion.Models.Workflow;

namespace Centurion.Models.Schema;

/// <summary>
/// IR (intermediate file *.centurion.json) format version constants and support matrix.
/// The IR is the only structured exchange contract in the command chain: schemaVersion identifies the structure version,
/// and older files can be explicitly upgraded to the current version via the <c>migrate</c> command.
/// </summary>
public static class CenturionSchema
{
    /// <summary>Tool name (written to generator.tool).</summary>
    public const string ToolName = "centurion";

    /// <summary>Current IR structure version (incremented on structural changes; historical versions enter the migration chain).</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>Numeric meta.version of legacy intermediate files (CenturionFileIO v1; only the meta/config/state fields).</summary>
    public const int LegacyMetaVersion = 1;

    /// <summary>Whether the version string is the currently supported version.</summary>
    public static bool IsSupported(string? version) =>
        string.Equals(version, CurrentVersion, StringComparison.Ordinal);
}

/// <summary>
/// IR root object: schemaVersion + generator (generator tool info) + provenance (processing traceability)
/// + config (immutable workflow configuration) + state (mutable workflow state).
/// All reads and writes of *.centurion.json use this object as the sole contract.
/// </summary>
public sealed class CenturionDocument
{
    /// <summary>IR structure version (e.g. "1.0"); used for compatibility checks when reading.</summary>
    public string SchemaVersion { get; set; } = CenturionSchema.CurrentVersion;

    /// <summary>Info about the tool that generated this file (version, command, time, input/output).</summary>
    public GeneratorInfo Generator { get; set; } = new();

    /// <summary>
    /// Processing traceability: steps executed during this file's lifecycle (operator + model + configuration fingerprint).
    /// Empty means no operators have been executed yet (e.g. from-script / convert products).
    /// </summary>
    public List<ProvenanceEntry> Provenance { get; set; } = [];

    /// <summary>Immutable workflow configuration (same as the legacy config).</summary>
    public WorkflowConfig Config { get; set; } = new();

    /// <summary>Mutable workflow state (same as the legacy state; Extensions are in-memory temporary data and are not persisted).</summary>
    public WorkflowState State { get; set; } = new();
}

/// <summary>Generator info: who generated this file, with which version, via which command, and at what time.</summary>
public sealed class GeneratorInfo
{
    /// <summary>Tool name (always "centurion").</summary>
    public string Tool { get; set; } = CenturionSchema.ToolName;

    /// <summary>Tool version at generation time (assembly InformationalVersion).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Name of the subcommand that triggered this save (e.g. "asr", "dub").</summary>
    public string? Command { get; set; }

    /// <summary>Generation time (ISO 8601, UTC with local offset).</summary>
    public string GeneratedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    /// <summary>Input file path for this processing run.</summary>
    public string? InputFile { get; set; }

    /// <summary>Output path of this intermediate file.</summary>
    public string? OutputFile { get; set; }
}

/// <summary>
/// A single provenance record: which operator executed a step, which model it used,
/// and which configuration it was based on (parameter fingerprint).
/// </summary>
public sealed class ProvenanceEntry
{
    /// <summary>Name of the operator/command that executed this step (e.g. "transcribe", "spellcheck").</summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>Model name used in this step (null when no model is used, e.g. audio conversion).</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Parameter fingerprint: SHA-256 hash (hex) of the workflow configuration this step was based on.
    /// Identical configuration yields an identical fingerprint; used to trace what parameters this result was generated with.
    /// </summary>
    public string? ParametersHash { get; set; }

    /// <summary>Completion time of this step (ISO 8601).</summary>
    public string? AppliedAt { get; set; }
}
