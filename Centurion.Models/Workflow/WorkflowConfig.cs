namespace Centurion.Models.Workflow;

/// <summary>
/// 工作流配置（不可变，合并了 MediaGenerationRequest + SplitOptions + 各 Payload 参数）
/// </summary>
public class WorkflowConfig
{
    // ---------- 输入/输出 ----------
    /// <summary>待处理音视频文件的完整路径。</summary>
    public string InputFilePath { get; init; } = string.Empty;

    /// <summary>触发本次运行的子命令名（如 "spawn"、"dub"），供质量报告等元信息使用。</summary>
    public string CommandName { get; init; } = string.Empty;
    /// <summary>作为校正基线的已有字幕文件路径（SRT/ASS 等），可为空。</summary>
    public string? SubtitleFilePath { get; init; }
    /// <summary>最终输出字幕文件路径，可为空（独立算子命令链中会被更新为当前命令的输出）。</summary>
    public string? OutputFilePath { get; set; }
    /// <summary>用于校正的脚本/文稿文件路径，可为空。</summary>
    public string? ScriptFilePath { get; init; }
    /// <summary>
    /// 推理设备偏好（auto/cpu/cuda/vulkan/directml）。
    /// Auto 时由系统自动检测（NVIDIA GPU → CUDA 构建的工具自动下载）。
    /// </summary>
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;
    /// <summary>字幕校正所采用的策略（仅时间轴/仅文本/两者）。</summary>
    public CorrectionStrategy CorrectStrategy { get; init; } = CorrectionStrategy.Both;
    /// <summary>允许的最大时间轴漂移（毫秒），超过即视为对齐异常。</summary>
    public int MaxDriftMs { get; init; } = 1500;
    /// <summary>模糊匹配的相似度阈值（0-1），低于此值不认为匹配成功。</summary>
    public double FuzzyThreshold { get; init; } = 0.72;
    /// <summary>Hunspell 拼写检查词典前缀（如 en_US），默认 en_US；缺失时自动下载（仅支持默认前缀）。</summary>
    public string HunspellDictionary { get; init; } = "en_US";
    /// <summary>脚本与音频词对齐所使用的映射策略标识（如 "rule"）。</summary>
    public string MapperStrategy { get; init; } = "rule";
    /// <summary>文本覆盖率阈值（0-1），低于此比例判定为对齐不充分。</summary>
    public double CoverageThreshold { get; init; } = 0.92;
    /// <summary>字幕最大每秒字符数（CPS），用于可读性约束。</summary>
    public double MaxCps { get; init; } = 5.0;
    /// <summary>单行字幕最大字符数。</summary>
    public int MaxCharsPerLine { get; init; } = 18;
    /// <summary>脚本缺失词的空隙是否以省略号 "[...]" 填充。</summary>
    public bool FillGapWithEllipsis { get; init; } = true;

    // ---------- 音频预处理 ----------
    /// <summary>音频预处理流水线的开关与阈值配置。</summary>
    public AudioPreprocessConfig AudioPreprocess { get; init; } = new();

    // ---------- 转录模块 ----------
    /// <summary>转录引擎选择（如 "crispasr"、"whisper"、"qwen"、"api"）。</summary>
    public string TranscriberEngine { get; init; } = "crispasr";   // whisper, qwen, api
    /// <summary>所选引擎使用的模型规格名（如 qwen3-asr-1.7b、large-v3）。</summary>
    public string? TranscriberModel { get; init; } = "qwen3-asr-1.7b";     // e.g., base, large
    /// <summary>云端 ASR 提供商名（openai/groq/dashscope/deepgram）；本地引擎忽略。</summary>
    public string AsrProvider { get; set; } = "crispasr";
    /// <summary>云端 ASR API 密钥。</summary>
    public string? AsrApiKey { get; init; }
    /// <summary>云端 ASR 端点；为空时按提供商默认。</summary>
    public string? AsrBaseUrl { get; init; }
    /// <summary>识别语言代码（如 "en"、"zh"）。</summary>
    public string Language { get; init; } = "en";
    /// <summary>喂给模型的初始提示词，用于引导风格/术语，可为空。</summary>
    public string? InitialPrompt { get; init; }

    // ---------- 分句模块 ----------
    /// <summary>分句策略（如 "rule"、"llm"、"nlp/catalyst"）。</summary>
    public string SplitStrategy { get; init; } = "rule";    // llm, rule, nlp/catalyst
    /// <summary>单句最大字符长度，超过则切分。</summary>
    public int MaxSentenceLength { get; init; } = 80;
    /// <summary>期望的目标句长（字符），切分时尽量靠拢。</summary>
    public int TargetSentenceLength { get; init; } = 50;
    /// <summary>句长在目标值附近允许的浮动范围。</summary>
    public int SpreadRange { get; init; } = 10;
    /// <summary>按时间轴合并相邻短句时的时间粒度（秒）。</summary>
    public float ChunkGranularity { get; init; } = 0.5f;
    /// <summary>相邻短句间隔小于该秒数时合并为一句。</summary>
    public double MergeGapSeconds { get; init; } = 1.5;
    /// <summary>是否在分句时重写/规整标点。</summary>
    public bool EnablePunctuationRewrite { get; init; } = true;
    /// <summary>LLM 分句时使用的模型名，可为空。</summary>
    public string? SplitterModel { get; init; }                 // 用于LLM
    /// <summary>LLM 分句所需的 API 密钥，可为空。</summary>
    public string? SplitterApiKey { get; init; }                // 用于LLM
    /// <summary>LLM 分句服务提供商名（如 deepseek、moonshot、openrouter；为空自动推断）。</summary>
    public string? SplitterProvider { get; init; }
    /// <summary>LLM 分句自定义端点；为空时使用所选提供商默认端点。</summary>
    public string? SplitterBaseUrl { get; init; }

    // ---------- 人声分离（可选增强，默认关闭） ----------
    /// <summary>
    /// 是否启用 Demucs 人声分离（将人声与伴奏/音乐分离后再转录）。
    /// 仅对含明显音乐/BGM 的素材有价值；纯语音素材开启会显著增加耗时。
    /// </summary>
    public bool VocalSeparation { get; set; } = false;
    /// <summary>
    /// Demucs 分离模型名（如 htdemucs），由 demucs-rs 首次运行时自动从 HuggingFace 下载缓存。
    /// </summary>
    public string VocalSeparationModel { get; set; } = "htdemucs";

    // ---------- OCR 模式（spawn --mode ocr）----------
    /// <summary>spawn OCR 模式：抽帧间隔（秒），默认 2 秒。</summary>
    public double OcrIntervalSeconds { get; init; } = 2.0;
    /// <summary>spawn OCR 模式：OCR 后端："zhipu"（云端 GLM-OCR，默认）| "ollama"（本地 Ollama 视觉模型）| "llamacpp"（本地 llama-server）。</summary>
    public string OcrBackend { get; init; } = "zhipu";
    /// <summary>spawn OCR 模式：OCR 模型名（默认随后端：zhipu→glm-ocr，ollama→qwen2.5vl:7b，llamacpp→local-model）。</summary>
    public string? OcrModel { get; init; }
    /// <summary>spawn OCR 模式：GLM-OCR API 密钥。</summary>
    public string? OcrApiKey { get; init; }
    /// <summary>spawn OCR 模式：GLM-OCR 端点地址（默认智谱 v4 chat/completions）。</summary>
    public string? OcrBaseUrl { get; init; }

    // ---------- 说话人分割 ----------
    /// <summary>
    /// 说话人分割后端："none"（关闭，默认）| "crispasr"（内置方法）| "pyannote"（Pyannote 分割 + TitaNet 嵌入）。
    /// </summary>
    public string DiarizationBackend { get; init; } = "none";
    /// <summary>
    /// crispasr 后端的分割方法：energy / xcorr / vad-turns / foxnose（默认 foxnose，精度最高且无需立体声）。
    /// </summary>
    public string DiarizationMethod { get; init; } = "foxnose";
    /// <summary>
    /// pyannote 后端使用的分割模型名（由 CrispASR 模型注册表自动下载，如 "pyannote-seg-3.0"）。
    /// </summary>
    public string DiarizationModel { get; init; } = "pyannote-seg-3.0";
    /// <summary>预期说话人数量，0 表示自动估计。</summary>
    public int NumSpeakers { get; init; } = 0;

    /// <summary>
    /// 说话人分割后处理：短于该秒数的片段视为碎片参与平滑合并，
    /// 消除逐段交替抖动与边界词错标。默认 0.5 秒。
    /// </summary>
    public double DiarizationMinSegmentSeconds { get; init; } = 0.5;

    // ---------- 输出风格 ----------
    /// <summary>是否输出卡拉OK模式（逐词 \k 时间标签）。</summary>
    public bool KaraokeMode { get; init; } = false;
    /// <summary>
    /// 是否在字幕文本前显示说话人标签（如 "[SPEAKER_01] 文本"）。
    /// 说话人信息始终写入 ASS 的 Name 字段；本开关仅控制文本前缀的显隐，默认开启。
    /// </summary>
    public bool ShowSpeakerLabels { get; init; } = true;

    // ---------- 其他 ----------
    /// <summary>模型/临时文件缓存目录。</summary>
    public string CacheDirectory { get; init; } = "./cache";
    /// <summary>是否启用词级强制对齐阶段。</summary>
    public bool EnableAlignment { get; init; } = true;
    /// <summary>强制对齐使用的模型名，可为空。</summary>
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";
    /// <summary>
    /// 强制对齐分段：相邻句子的时间间隙超过该秒数时，把句子切分为独立的分段块。
    /// 分段后每块仅启动一次对齐进程，显著降低长音频的进程/模型加载开销。默认 2.0 秒。
    /// </summary>
    public double AlignmentChunkGapSeconds { get; init; } = 2.0;
    /// <summary>
    /// 强制对齐分段：单个分段块的最大音频时长（秒）。
    /// 超过后强制另起一块，避免单次对齐超出模型的上下文窗口。默认 120 秒。
    /// </summary>
    public double AlignmentMaxChunkSeconds { get; init; } = 120.0;

    // ---------- 对齐前文本清洗 ----------
    /// <summary>是否在对齐前启用文本清洗。</summary>
    public bool EnableTextCleaning { get; init; } = true;
    /// <summary>清洗时是否移除标点。</summary>
    public bool RemovePunctuation { get; init; } = false;
    /// <summary>清洗时是否把数字展开为文字。</summary>
    public bool ExpandNumbers { get; init; } = true;
    /// <summary>清洗时是否把缩写展开为全称。</summary>
    public bool ExpandAbbreviations { get; init; } = false;
    /// <summary>自定义清洗词典文件路径，可为空。</summary>
    public string? CustomDictPath { get; init; }

    // ---------- 翻译模块（translate 子命令） ----------
    /// <summary>翻译的目标语言代码（如 zh、en、ja），translate 命令必填。</summary>
    public string TargetLanguage { get; init; } = string.Empty;
    /// <summary>翻译策略名称（如 llm），默认 llm。</summary>
    public string TranslationStrategy { get; init; } = "llm";
    /// <summary>LLM 翻译使用的模型名，可为空（OpenAI 默认 gpt-4o-mini，Ollama 默认 llama3.1）。</summary>
    public string? TranslationModel { get; init; }
    /// <summary>LLM 翻译所需的 API 密钥；为空时回退本地 Ollama。</summary>
    public string? TranslationApiKey { get; init; }
    /// <summary>LLM 翻译服务提供商名（如 deepseek、moonshot、openrouter；为空自动推断）。</summary>
    public string? TranslationProvider { get; init; }
    /// <summary>LLM 翻译自定义端点；为空时使用所选提供商默认端点。</summary>
    public string? TranslationBaseUrl { get; init; }
    /// <summary>术语表文件路径（JSON：源语言术语与目标语言术语的映射），可为空。</summary>
    public string? GlossaryPath { get; init; }
    /// <summary>目标语言台本文件路径（每行一句目标语言译文），可为空。</summary>
    public string? TargetScriptPath { get; init; }
    /// <summary>是否输出双语字幕（原文 \\N 译文），默认仅目标语言。</summary>
    public bool Bilingual { get; init; } = false;

    // ── dub（媒体译制）相关 ──

    /// <summary>TTS 引擎（当前仅 "llama"，即 llama.cpp llama-tts）。</summary>
    public string TtsEngine { get; init; } = "llama";

    /// <summary>TTS 模型（metadata.json Models 中注册的名称，如 1.7b-base-q4）。</summary>
    public string TtsModel { get; init; } = "1.7b-base-q4";

    /// <summary>配音目标语言（ISO 639-1，如 zh/en/ja），用于 llama-tts --tts-lang。</summary>
    public string TtsLanguage { get; init; } = "zh";

    /// <summary>可选：手动指定说话人参考音频目录（每说话人一个 wav，文件名 SPEAKER_xx.wav）。</summary>
    public string? SpeakerReferenceDir { get; init; }

    /// <summary>可选：双语模式下的译文轨文件路径（与主字幕按时间窗匹配）。</summary>
    public string? TranslationSubtitlePath { get; init; }

    /// <summary>TTS 语速调整范围之外时使用的处理策略（true=钳制到 0.5x~2.0x 边界，false=保留原合成时长）。</summary>
    public bool DubStrictTiming { get; init; } = true;

    /// <summary>可选：伴奏/背景音频路径（提供后启用 ducking 混音）。</summary>
    public string? DubBackgroundPath { get; init; }

    /// <summary>输出响度目标（LUFS，默认 -16）。</summary>
    public double DubLoudnessTarget { get; init; } = -16;

    /// <summary>TTS 合成并行度（默认 2；内存吃紧时调 1）。</summary>
    public int TtsParallelism { get; init; } = 2;

    /// <summary>长句分块阈值（秒，默认 15；目标时长超过时按比例拆分子段）。</summary>
    public double DubMaxChunkSeconds { get; init; } = 15;

    /// <summary>是否启用 ducking（有伴奏时默认开启）。</summary>
    public bool DubDucking { get; init; } = true;
}
