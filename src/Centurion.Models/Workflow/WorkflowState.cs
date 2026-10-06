using System.Text.Json.Serialization;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Providers;

namespace Centurion.Models.Workflow;

/// <summary>
/// Workflow state; mutable and merging the data fields of all Response objects.
/// Field layering contract:
/// - Formal fields: stage products and flags shared by multiple operators/command chains, persisted with the IR for checkpoint recovery.
/// - Runtime fields ([JsonIgnore]): strongly typed in-process data shared across operators but never written into the IR.
/// - Extensions: generic temporary slots for single-operator private or edge data, not written into the IR.
/// </summary>
public class WorkflowState
{
    // ---------- Audio preparation chain; each stage path is produced by an upstream operator and consumed by a downstream one ----------
    /// <summary>Temporary working directory dedicated to this pipeline.</summary>
    public string? PipelineTempDirectory { get; set; }
    /// <summary>Path of the temporary file after FFmpeg resampling/conversion.</summary>
    public string? ConvertedAudioPath { get; set; }
    /// <summary>Path of the audio after preprocessing, such as resampling, denoising, or normalization.</summary>
    public string? PreprocessedAudioPath { get; set; }
    /// <summary>Path of the vocal track after Demucs vocal separation; consumed first by transcription and diarization.</summary>
    public string? VocalsPath { get; set; }

    // ---------- Sentence lists per stage; word-level Speaker and timestamps are filled in step by step by downstream operators ----------
    /// <summary>Sentences just transcribed, with no speaker information yet.</summary>
    public List<Sentence> TranscribeSentences { get; set; } = [];
    /// <summary>The original subtitle sentences used as the correction baseline.</summary>
    public List<Sentence> SubtitleSentences { get; set; } = [];
    /// <summary>Sentences after script/subtitle correction.</summary>
    public List<Sentence> CorrectedSentences { get; set; } = [];
    /// <summary>Sentences after splitting and merging, with no speaker information yet.</summary>
    public List<Sentence> SplitSentences { get; set; } = [];
    /// <summary>Sentences completed with speaker labels, with each Word carrying a Speaker.</summary>
    public List<Sentence> DiarizedSentences { get; set; } = [];
    /// <summary>Sentences completed with word-level forced alignment, with corrected timestamps.</summary>
    public List<Sentence> AlignedSentences { get; set; } = [];
    /// <summary>Sentences at the coarse, unrefined stage; may be null.</summary>
    public List<Sentence>? CoarseSentences { get; set; }
    /// <summary>Imported script or transcript sentences.</summary>
    public List<Sentence> ScriptSentences { get; set; } = [];
    /// <summary>The set of sentences currently being processed, serving as input for downstream operators.</summary>
    public List<Sentence> CurrentSentences { get; set; } = [];
    /// <summary>Actual coverage of word mapping between script and audio, from 0 to 1; used by the quality report and threshold alerts.</summary>
    public double MapperCoverage { get; set; }

    // ---------- Translation (optional) ----------
    /// <summary>List of translated sentences, with the translation written back per sentence into Sentence.TranslatedText; null when translation is disabled.</summary>
    public List<Sentence>? TranslatedSentences { get; set; }

    /// <summary>Translation QA metrics such as glossary hit rate and length deviation; written by TranslationOperator and consumed by the quality report.</summary>
    public TranslationQa? TranslationQa { get; set; }

    // ---------- Stage completion flags; checkpoint recovery and IR provenance derivation ----------
    /// <summary>Whether the audio conversion stage has completed.</summary>
    public bool IsAudioConverted { get; set; }
    /// <summary>Whether the vocal separation stage has completed.</summary>
    public bool IsVocalsSeparated { get; set; }
    /// <summary>Whether the transcription stage has completed.</summary>
    public bool IsTranscribed { get; set; }
    /// <summary>Whether the sentence splitting stage has completed.</summary>
    public bool IsSplit { get; set; }
    /// <summary>Whether the diarization stage has completed.</summary>
    public bool IsDiarized { get; set; }
    /// <summary>Whether the forced alignment stage has completed.</summary>
    public bool IsAligned { get; set; }
    /// <summary>Whether the translation stage has completed.</summary>
    public bool IsTranslated { get; set; }

    // ---------- Dub Data ----------
    /// <summary>Dub segments: segment-level data combining speaker profiling, TTS synthesis, time alignment, and mixing.</summary>
    public List<DubSegment> DubSegments { get; set; } = [];
    /// <summary>Path of the final dubbed wav output; written by DubCommand and consumed by AudioMix output and command teardown.</summary>
    public string? DubOutputWavPath { get; set; }
    /// <summary>Mapping of speaker to reference audio path; produced by SpeakerProfiling and consumed by TTS synthesis.</summary>
    public Dictionary<string, string> DubSpeakerReferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---------- ASS Styles ----------
    /// <summary>
    /// ASS style table in the [V4+ Styles] section. Written when convert parses ASS and preferred when build renders ASS;
    /// when empty the renderer falls back to built-in default styles. Edited by the Studio front end through intermediate files.
    /// </summary>
    public List<AssStyle> Styles { get; set; } = [];

    // ---------- Diagnostics ----------
    /// <summary>List of error messages accumulated during the run.</summary>
    public List<string> Errors { get; set; } = [];
    /// <summary>List of warning messages accumulated during the run.</summary>
    public List<string> Warnings { get; set; } = [];
    /// <summary>Statistics report produced by the correction stage.</summary>
    public CorrectionReport Report { get; set; } = new();

    // ---------- Runtime fields; in-process data shared across operators, not written into the IR ----------
    /// <summary>Execution time of each operator in the pipeline; written by PipelineExecutor and consumed by the correction report and others.</summary>
    [JsonIgnore]
    public Dictionary<string, TimeSpan> StepTimings { get; set; } = new();

    /// <summary>Metadata of the correction/alignment process, tracking retimed/drift per sentence; Sentence is the reference key and is valid in-process only.</summary>
    [JsonIgnore]
    public Dictionary<Sentence, Dictionary<string, object>> CorrectionMetadata { get; set; } = new();

    /// <summary>
    /// Aggregated Provider call usage: tokens, audio seconds, cache hits, estimated cost.
    /// Written by operators executed through the Provider abstraction, such as transcription; command teardown prints the run statistics.
    /// In-process only and not written into the IR.
    /// </summary>
    [JsonIgnore]
    public List<ProviderUsage> ProviderUsages { get; set; } = [];

    // ---------- Extension slots; single-operator private or edge data, not written into the IR ----------
    /// <summary>
    /// Extension dictionary for passing unconventional temporary data between operators.
    /// For single-operator private or edge data only, such as audio-probe diagnostics SourceAudioInfo/EstimatedSnrDb and
    /// one-off results like track check, spell check, and quality report paths; in-process only and not persisted into the IR.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, object> Extensions { get; set; } = new();
}
