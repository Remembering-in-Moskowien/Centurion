using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Centurion.Models.Schema;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// Builds a <see cref="CenturionDocument"/> (generator + provenance + config/state) from a
/// <see cref="SubtitleWorkflowContext"/>, used uniformly whenever the command chain saves the IR.
/// </summary>
public static class CenturionDocumentBuilder
{
    /// <summary>Builds a document from the workflow context and the current command.</summary>
    public static CenturionDocument Create(
        SubtitleWorkflowContext context,
        string commandName,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new CenturionDocument
        {
            Generator = new GeneratorInfo
            {
                Version = ToolVersion,
                Command = commandName,
                InputFile = context.Config.InputFilePath,
                OutputFile = outputPath
            },
            Config = context.Config,
            State = context.State,
            Provenance = BuildProvenance(context.State, context.Config)
        };
    }

    /// <summary>
    /// Deduces the processing steps that have run from the state flags (one provenance record per
    /// operator). Step order follows the natural pipeline order: convert → vocal separation →
    /// transcribe → split → diarize → align → translate → dub.
    /// </summary>
    private static List<ProvenanceEntry> BuildProvenance(WorkflowState state, WorkflowConfig config)
    {
        var entries = new List<ProvenanceEntry>(8);
        var hash = ParametersHash(config);
        var appliedAt = DateTimeOffset.Now.ToString("O");

        void Add(string operatorName, string? model) =>
            entries.Add(new ProvenanceEntry
            {
                Operator = operatorName,
                Model = model,
                ParametersHash = hash,
                AppliedAt = appliedAt
            });

        if (state.IsAudioConverted) Add("audio-convert", null);
        if (state.IsVocalsSeparated) Add("vocal-separation", config.VocalSeparationModel);
        if (state.IsTranscribed) Add("transcribe", config.TranscriberModel);
        if (state.IsSplit) Add("split", config.SplitterModel);
        if (state.IsDiarized) Add("diarize", config.DiarizationModel);
        if (state.IsAligned) Add("align", config.AlignmentModel);
        if (state.IsTranslated) Add("translate", config.TranslationModel);
        if (state.DubSegments.Count > 0) Add("dub", config.TtsModel);

        return entries;
    }

    /// <summary>
    /// SHA-256 fingerprint of the workflow config (hex): hashes the config's canonical JSON, so
    /// identical configs yield identical fingerprints; used to trace "what parameters this result
    /// was generated from".
    /// </summary>
    public static string ParametersHash(WorkflowConfig config)
    {
        var json = JsonSerializer.Serialize(config, CenturionJsonContext.Default.WorkflowConfig);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Current tool version: uniformly the build number (build-N); falls back to the assembly InformationalVersion when missing.</summary>
    private static string ToolVersion => Centurion.Core.Utils.Infrastructure.BuildInfo.DisplayVersion;
}
