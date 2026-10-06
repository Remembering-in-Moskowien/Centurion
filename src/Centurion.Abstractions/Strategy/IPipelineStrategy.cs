namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Declared capabilities of a pipeline strategy. Assembled DAGs read these at build time to
/// personalize the graph: a node is added, skipped, or reconfigured based on what the active
/// strategy already produces (e.g. a transcriber that already outputs forced-aligned word
/// timestamps does not need a separate alignment stage).
/// </summary>
[Flags]
public enum StrategyCapabilities
{
    /// <summary>No special capabilities; the pipeline graph is assembled generically.</summary>
    None = 0,

    /// <summary>
    /// The transcription output already contains forced-aligned word-level timestamps
    /// (e.g. CrispASR-Qwen3 runs with the Qwen3 forced aligner attached). The pipeline may
    /// skip the standalone "Force Alignment" stage even when alignment was requested.
    /// </summary>
    AlignedTimestamps = 1 << 0,

    /// <summary>
    /// The strategy itself produces speaker labels (reserved; e.g. a streaming ASR that
    /// natively attributes words to speakers), so an independent diarization stage is redundant.
    /// </summary>
    SpeakerLabels = 1 << 1,
}

/// <summary>
/// Common surface of every pipeline strategy: a display name plus a declared capability set.
/// Strategies are resolved once at pipeline-assembly time; the capabilities let the assembler
/// express strategy-specific behavior (skip redundant stages, change node wiring) declaratively.
/// </summary>
public interface IPipelineStrategy
{
    /// <summary>Display name of the strategy, used in logs and reports.</summary>
    string StrategyName { get; }

    /// <summary>Declared capabilities; the DAG assembler uses them to tailor the pipeline.</summary>
    StrategyCapabilities Capabilities { get; }
}
