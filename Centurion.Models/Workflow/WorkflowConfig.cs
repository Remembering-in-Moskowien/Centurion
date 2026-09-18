namespace Centurion.Models.Workflow;

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
    /// <summary>
    /// 推理设备偏好（auto/cpu/cuda/vulkan/directml）。
    /// Auto 时由系统自动检测（NVIDIA GPU → CUDA 构建的工具自动下载）。
    /// </summary>
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;
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

    // ---------- 人声分离（可选增强，默认关闭） ----------
    /// <summary>
    /// 是否启用 Demucs 人声分离（将人声与伴奏/音乐分离后再转录）。
    /// 仅对含明显音乐/BGM 的素材有价值；纯语音素材开启会显著增加耗时。
    /// </summary>
    public bool VocalSeparation { get; init; } = false;
    /// <summary>
    /// Demucs 分离模型名（如 htdemucs），由 demucs-rs 首次运行时自动从 HuggingFace 下载缓存。
    /// </summary>
    public string VocalSeparationModel { get; init; } = "htdemucs";

    // ---------- 说话人分割 ----------
    /// <summary>
    /// 说话人分割后端："none"（关闭）| "crispasr"（内置方法）| "pyannote"（Pyannote 分割 + TitaNet 嵌入）。
    /// </summary>
    public string DiarizationBackend { get; init; } = "crispasr";
    /// <summary>
    /// crispasr 后端的分割方法：energy / xcorr / vad-turns / foxnose（默认 foxnose，精度最高且无需立体声）。
    /// </summary>
    public string DiarizationMethod { get; init; } = "foxnose";
    /// <summary>
    /// pyannote 后端使用的分割模型名（由 CrispASR 模型注册表自动下载，如 "pyannote-seg-3.0"）。
    /// </summary>
    public string DiarizationModel { get; init; } = "pyannote-seg-3.0";
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
