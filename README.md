# Centurion

> 自动化字幕生成工具（开发中）
>
> Centurion 是一个基于 .NET 10 的命令行工具，旨在从媒体文件生成带有精确词级时间戳的 ASS 字幕。项目采用“一切皆算子”的管道式设计，集成 Whisper 转录、智能分句、说话人分割等模块，目前处于积极开发阶段。

---

## 🚧 当前状态

- **版本**：尚未发布稳定版，您可以从 [GitHub Releases](https://github.com/2128611819qqcom/Centurion/releases) 获取预发布包（Pre-release）。
- **支持**：暂不支持 GPU 加速，所有推理均运行在 CPU 上。
- **可用命令**：主要命令为 `spawn`（核心字幕生成），`convert` 为辅助格式转换命令。
- **模型管理**：首次运行时会自动下载所需模型，请确保网络畅通。
- **强制对齐**：原计划中的 `--align` 功能**当前不可用**，该选项已被禁用（会被忽略）。我们将在后续版本中重新评估并修复。

---

## ✨ 主要特性

- **端到端字幕生成**：输入任意音视频文件，一键输出 ASS 字幕。
- **高性能 CPU 转录**：基于 FasterWhisper.NET 的本地引擎，无需 GPU 即可获得可接受的速度。
- **智能分句**：利用 Catalyst NLP 进行语义分割，提升可读性。
- **说话人区分**：通过 sherpa‑onnx 实现说话人分割，为每句字幕标注角色。
- **卡拉OK 模式**：生成的 ASS 字幕包含 `\K` 标签，可在支持 ASS 的播放器中逐词高亮。

---

## 🖥️ 使用命令
### `spawn` – 主要字幕生成命令
该命令从媒体文件生成完整字幕，支持多种选项。

```bash
Centurion.Cli spawn -i <input> [options]
```
**常用选项**

| 参数            |	说明|
|-----------------|-----|
| -i, --input     |	输入文件路径（必填）|
| -o, --output    |	输出 ASS 文件路径（默认输入文件名 + .ass）
| -l, --language  |	语言代码，如 en, zh, ja（默认 en）
| -m, --model     |	Whisper 模型：tiny, base, small, medium, large-v3（默认 base）
| -p, --prompt    |	Whisper 初始提示词
| --max-length    |	每行字幕最大字符数（默认 80）
| --target-length |	目标字符数（默认 50）
| --spread-range  |	长度分布扩散范围（默认 10）
| --num-speakers  |	说话人数量（0 为自动检测，默认 0）
| -k, --karaoke   |	启用卡拉OK模式（生成 \K 标签）
> 注意：--align 选项目前已被禁用，即使指定也会被忽略。我们将在未来版本中视情况恢复。

### 示例：

```bash
#生成英文字幕（基础）
Centurion spawn -i video.mp4 -l en

#生成中文卡拉OK字幕
Centurion spawn -i lecture.mp4 -o subs.ass -l zh --karaoke
```
### `convert` – 辅助转换命令
该命令用于简单的字幕格式转换或内容调整（当前功能有限），具体用法请参阅 Centurion convert --help。

注意：convert 是次要辅助命令，主要功能由 spawn 提供。

## 📦 安装
从 GitHub Releases 获取预发布包
访问项目的 Releases 页面（请替换为实际地址）。

下载适用于您操作系统的最新预发布压缩包（如 Centurion-win-x64.zip、Centurion-linux-x64.tar.gz 等）。

解压到任意目录，将可执行文件所在路径添加到 PATH 环境变量，或直接使用完整路径运行。

预发布版本包含开发中的新特性，可能存在未完善的细节，欢迎反馈。

从源码构建（不推荐，仅供开发）
若需自行构建，请确保已安装 .NET 10 SDK，然后执行：

```bash
git clone <repository-url>
cd Centurion
dotnet build -c Release
```
构建产物位于 Centurion.Cli/bin/Release/net10.0/。

## 🧠 模型管理
首次运行 spawn 时，工具会自动下载所需模型至 models/ 目录：

text
models/
├── fasterwhisper/          # Whisper 目录模型
│   └── base/
│       ├── config.json
│       ├── model.bin
│       └── vocabulary.txt
└── diarization/            # 说话人分割模型
    └── voxceleb_resnet293_LM.onnx
所有模型均从 Hugging Face 镜像（hf-mirror.com）下载，无需手动干预。

## ⚠️ 注意事项
项目处于开发阶段，命令和参数可能会发生变化，请以实际运行结果为准。

目前仅支持 CPU 推理，处理较长音频时 CPU 负载较高，建议在性能较好的机器上运行。

强制对齐（--align）已被禁用，该功能当前不可用，未来可能重新评估。

若模型下载失败，可检查网络代理设置；如有自定义镜像源需求，可通过环境变量 CENTURION_MODEL_MIRROR 指定（未实现，可后续扩展）。

## 🤝 贡献与反馈
欢迎提交 Issue 和 Pull Request。因项目仍在早期，请先通过 Issue 讨论大的功能改动，避免无效工作。