namespace Centurion.Core.Models.Workflow;

/// <summary>
/// 工作流配置（不可变，合并了 MediaGenerationRequest + SplitOptions + 各 Payload 参数）
/// </summary>
public class WorkflowConfig
{
    // ---------- 输入/输出 ----------
    public string InputFilePath { get; init; } = string.Empty;
    public string? SubtitleFilePath { get; init; }
    public string? OutputFilePath { get; init; }
    public string? ScriptFilePath { get; init; }
    public CorrectionStrategy CorrectStrategy { get; init; } = CorrectionStrategy.Both;
    public int MaxDriftMs { get; init; } = 1500;
    public double FuzzyThreshold { get; init; } = 0.72;
    public string MapperStrategy { get; init; } = "rule";
    public double CoverageThreshold { get; init; } = 0.92;
    public double MaxCps { get; init; } = 5.0;
    public int MaxCharsPerLine { get; init; } = 18;
    public bool FillGapWithEllipsis { get; init; } = true;

    // ---------- 音频预处理 ----------
    public AudioPreprocessConfig AudioPreprocess { get; init; } = new();

    // ---------- 转录模块 ----------
    public string TranscriberEngine { get; init; } = "crispasr";   // whisper, qwen, api
    public string? TranscriberModel { get; init; } = "qwen3-asr-1.7b";     // e.g., base, large
    public string Language { get; init; } = "en";
    public string? InitialPrompt { get; init; }

    // ---------- 分句模块 ----------
    public string SplitStrategy { get; init; } = "rule";    // llm, rule, nlp/catalyst
    public int MaxSentenceLength { get; init; } = 80;
    public int TargetSentenceLength { get; init; } = 50;
    public int SpreadRange { get; init; } = 10;
    public float ChunkGranularity { get; init; } = 0.5f;
    public double MergeGapSeconds { get; init; } = 1.5;
    public bool EnablePunctuationRewrite { get; init; } = true;
    public string? SplitterModel { get; init; }                 // 用于LLM
    public string? SplitterApiKey { get; init; }                // 用于LLM

    // ---------- 说话人分割（保留，但可后续独立） ----------
    public string DiarizationModel { get; init; } = "voxceleb_resnet293_LM";
    public int NumSpeakers { get; init; } = 0;

    // ---------- 输出风格 ----------
    public bool KaraokeMode { get; init; } = false;

    // ---------- 其他 ----------
    public string CacheDirectory { get; init; } = "./cache";
    public bool EnableAlignment { get; init; } = true;
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";

    // ---------- 对齐前文本清洗 ----------
    public bool EnableTextCleaning { get; init; } = true;
    public bool RemovePunctuation { get; init; } = false;
    public bool ExpandNumbers { get; init; } = true;
    public bool ExpandAbbreviations { get; init; } = false;
    public string? CustomDictPath { get; init; }
}
