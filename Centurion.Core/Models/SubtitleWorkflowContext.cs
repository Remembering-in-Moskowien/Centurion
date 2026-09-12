// File: Centurion.Core/Models/SubtitleWorkflowContext.cs
﻿
// 引用 WhisperTranscriptJSON

namespace Centurion.Core.Models;

/// <summary>
/// 字幕生成工作流的全量上下文（Pipeline 唯一传递对象）
/// 设计为纯数据容器，可序列化以支持检查点/断点续传。
/// </summary>
public class SubtitleWorkflowContext(WorkflowConfig config)
{
    /// <summary>不可变的用户配置（源自 CLI SubCommand）</summary>
    public WorkflowConfig Config { get; init; } = config;

    /// <summary>可变的工作流状态（由各 Operator 逐步填充）</summary>
    public WorkflowState State { get; set; } = new();
}

// ============================================================
// 1. 配置部分（不可变，合并了 MediaGenerationRequest + SplitOptions + 各 Payload 参数）
// ============================================================
public class WorkflowConfig
{
    // ---------- 输入/输出 ----------
    public string InputFilePath { get; init; } = string.Empty;
    public string? OutputFilePath { get; init; }
    public string? ScriptFilePath { get; init; }
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
    public string? AlignmentModel { get; init; }

    // ---------- 对齐前文本清洗 ----------
    public bool EnableTextCleaning { get; init; } = true;
    public bool RemovePunctuation { get; init; } = false;
    public bool ExpandNumbers { get; init; } = true;
    public bool ExpandAbbreviations { get; init; } = false;
    public string? CustomDictPath { get; init; }
}

// ============================================================
// 2. 状态部分（可变，合并了所有 Response 的数据字段）
// ============================================================
public class WorkflowState
{
    // ---------- 原始音频路径（由 Config.InputFilePath 派生，但保留以便存储转换后的路径） ----------
    public string? PipelineTempDirectory { get; set; } 
    public string? ConvertedAudioPath { get; set; } // FFmpeg 重采样/转换后的临时文件路径
    public string? PreprocessedAudioPath { get; set; }
    public AudioProbeInfo? SourceAudioInfo { get; set; }
    public AudioProbeInfo? PreprocessedAudioInfo { get; set; }
    public double? EstimatedSnrDb { get; set; }
    public bool NoiseReductionApplied { get; set; }
    // ---------- 各阶段处理后的句子列表 ----------
    // 注意：Sentence 中的 Word 对象会逐步被下游算子补充 Speaker 和精确时间戳。
    public List<Sentence> TranscribeSentences { get; set; } = []; // 刚转录完，无说话人信息
    public List<Sentence> SplitSentences { get; set; } = []; // 分句后（合并/切分），无说话人信息
    public List<Sentence> DiarizedSentences { get; set; } = []; // 说话人标注后（每个 Word 带 Speaker）
    public List<Sentence> AlignedSentences { get; set; } = []; // 强制对齐后（词级时间戳修正）
    public List<Sentence>? CoarseSentences { get; set; }
    public List<Sentence> ScriptSentences { get; set; } = [];
    public List<Sentence> CurrentSentences { get; set; } = [];
    public double MapperCoverage { get; set; }
    
    // ---------- 转换专用数据槽：已移除 SubtitlesParserV2 模型，统一使用 Sentence ----------
    // （转换后的原始字幕将存入 TranscribeSentences，与转录结果同构）

    // ---------- 翻译结果（可选） ----------
    public List<Sentence>? TranslatedSentences { get; set; }

    // ---------- 阶段完成标志（用于检查点恢复） ----------
    public bool IsAudioConverted { get; set; }
    public bool IsTranscribed { get; set; }
    public bool IsSplit { get; set; }
    public bool IsDiarized { get; set; }
    public bool IsAligned { get; set; }
    
    public bool IsTranslated { get; set; }
    
    public bool IsFinalized { get; set; }

    // ---------- 运行时诊断信息 ----------
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    // ---------- 扩展数据槽（用于算子间临时传递非常规数据，避免改上下文结构） ----------
    public Dictionary<string, object> Extensions { get; set; } = new();
}


public enum AudioNoiseReductionBackend
{
    BuiltInFfmpeg,
    ExternalCli
}

public record AudioPreprocessConfig
{
    public bool EnableResampling { get; init; } = true;
    public bool EnableDownmixing { get; init; } = true;
    public bool EnableHighPass { get; init; } = true;
    public bool EnableLoudnessNormalization { get; init; } = true;
    public bool EnableNoiseReduction { get; init; } = false;
    public double SnrThresholdDb { get; init; } = 15.0;
    public AudioNoiseReductionBackend NoiseReductionBackend { get; init; } = AudioNoiseReductionBackend.BuiltInFfmpeg;
}

public record AudioProbeInfo(int SampleRate, int Channels, string Codec, string Format);