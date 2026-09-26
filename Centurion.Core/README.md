# Centurion.Core 结构说明

`Centurion.Core` 实现工作流、能力策略和运行时基础设施。源码按架构层次分组，根目录只保留 5 个分层目录：

```
Centurion.Core/
├── Workflow/        # 工作流层：编排、阶段算子、策略与组装
├── Capabilities/    # 能力层：外部能力适配、工具管理与横切服务
├── Processing/      # 文本处理层：文本工具与拼写检查
├── Operators/       # 兼容算子层：旧式请求/响应算子（保留，不扩展）
└── Utils/           # 工具层：解析、媒体、报告、序列化、基础设施辅助
```

## Workflow/ — 工作流层

- `Pipeline/`：工作流执行器（`PipelineExecutor`）、对齐基类以及真实的 pipeline stage 算子（`Pipeline/Operators/`）。
- `Strategy/`：转录、分句、对齐、说话人分离和翻译策略。
- `Factories/`：根据配置组装策略和 pipeline 算子（策略工厂 + `PipelineOperatorFactory`）。
- `DependencyInjection/`：集中注册 Core 服务（`AddCenturionCore`）。

## Capabilities/ — 能力层

- `Infrastructure/Asr/`、`Infrastructure/Llm/`、`Infrastructure/Ocr/`：ASR、LLM、OCR 的端点解析、客户端和适配策略。
- `Infrastructure/Tts/`：TTS 引擎适配。
- `Infrastructure/`：设备检测等跨能力基础设施。
- `Managers/Runtime/`：进程和临时目录管理。
- `Managers/Media/`：媒体工具、模型和编码器管理。
- `Managers/Tools/`：外部推理工具和 VideoSubFinder 管理。
- `Update/`：版本检查和应用更新。
- `Logging/`、`Localization/`：日志与本地化实现。

## Processing/ — 文本处理层

- `Text/`：分词、文本相似度、校正键等纯文本处理。
- `SpellCheck/`：Hunspell 拼写检查。

## 工具层与其他

- `Utils/Parsing/`：结构化数据解析、术语表和文本/时间线辅助处理。
- `Utils/Media/`：媒体轨道检查、字幕提取和 WAV 信噪比估算等媒体分析辅助。
- `Utils/Reporting/`：质量报告、字幕格式渲染和工作流上下文导出。
- `Utils/Serialization/`：Centurion IR 文件读写。
- `Utils/Infrastructure/`：二进制定位、模型路径、缓存、下载代理和校验辅助。
- `Utils/` 根目录不再放置散落的工具实现。

## 兼容算子

- `Operators/Download/`、`Operators/Media/`、`Operators/Subtitles/`：旧式请求/响应算子，按下载、媒体处理和字幕转换分组。新工作流阶段应放在 `Workflow/Pipeline/Operators/`，不要扩展旧式算子集合。

新增代码应优先归入明确的层与子目录；只有真正跨领域且职责简单的辅助逻辑才适合留在通用目录。


## IR 中间文件契约（*.centurion.json，schema 1.0）

- 根对象 `CenturionDocument`（`Centurion.Models.Schema`）：`schemaVersion` + `generator`（工具/版本/命令/时间）+ `provenance`（每步骤 operator / 模型 / 参数 SHA-256 指纹）+ `config` + `state`。
- 序列化：System.Text.Json **源生成器**（`CenturionJsonContext`，无反射）；Extensions 为进程内临时数据，不持久化。
- 读写/校验/迁移统一走 `ICenturionDocumentStore`（`Centurion.Core.Utils.Serialization`）；`validate` 前置校验，`migrate` 升级旧版 meta/config/state 格式。
- JSON Schema：`schemas/centurion-v1.json`（由 `tools/schema-gen` 经 `CenturionSchemaExporter` 生成；结构变更后重跑该工具同步）。

## Provider 抽象（`Providers/`，第⑨阶段）

- **契约**（`Centurion.Abstractions.Providers`）：6 域接口 `IAsrProvider / IOcrProvider / ILlmProvider / ITtsProvider / IDiarizationProvider / IVocalSeparationProvider`，全部继承 `IProvider`（`Name / DisplayName / Capabilities / IsAvailableAsync`）。
- **数据契约**（`Centurion.Models.Providers`）：`ProviderCapabilities`（本地/云、语言、GPU、成本、延迟、质量档）、`ProviderUsage`（token/音频秒/缓存/估算成本，含聚合 `+`）、`ProviderResult<T>` 与 `ProviderUnavailableException / ProviderExecutionException`。
- **实现**（`Centurion.Core.Providers/`）：本地 ASR 3 变体（whispercpp / crispasr-qwen / crispasr-whisper）+ 云 ASR 4（openai/groq/dashscope/deepgram）、OCR 4（zhipu/ollama/llamacpp/**rapidocr** 本地 PaddleOCR ONNX）、LLM 11（OpenAI 兼容 chat/completions）、TTS（llama-tts）、说话人分割 2（crispasr/pyannote）、人声分离（demucs）。
- **装配**：`ProviderRegistry`（注册表）+ `ProviderFactory`（按配置解析 fallback 链：云优先→本地兜底 / 本地优先→云备用；`--profile offline/fast/quality/cheap` 调节主备与预算取向）。
- **横切**：`ProviderPolicies`（指数退避重试/熔断/限流/跨链预算闸门）+ `ApiKeyStore`（显式配置 → 环境变量 `CENTURION_<域>_API_KEY/_BASE_URL` → null；无密钥时云端 `IsAvailable=false`，链自动回退本地，不崩溃）。
- **命令**：`Centurion models list/install/verify/remove`（模型注册表管理）、`Centurion providers list/test`（能力与可用性探测）。
- **执行接入**：`TranscribeOperator` 沿 Provider 链转录；运行结束输出 `tokens in/out、音频分钟、缓存命中、估算成本`（聚合进 `WorkflowState.ProviderUsages`，`QualityReportOperator` 收尾打印）。