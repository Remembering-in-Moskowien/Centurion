using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Workflow.Factories;using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models.Workflow;
using Centurion.Core.Utils.Serialization;
using Microsoft.Extensions.Logging;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>transcribe</c> 独立算子命令：媒体/中间文件 → 只执行 转换 + 预处理 + 转录。
/// 与打包命令 spawn 的区别：不做分句、清洗、对齐与质量报告。
/// </summary>
public sealed class TranscribeCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    PipelineOperatorFactory operatorFactory,
    ILogger<TranscribeCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "transcribe";

    /// <inheritdoc />
    protected override bool AcceptsMedia => true;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config) =>
        [ffmpegOp, audioPreprocessOp, operatorFactory.CreateTranscribeOperator(config)];
}

/// <summary>
/// <c>vocalsep</c> 独立算子命令：媒体/中间文件 → 只执行 转换 + 人声分离（Demucs）。
/// 分离后的人声轨拷贝为 &lt;输入名&gt;.vocals.wav 供后续转录/说话人分割使用。
/// </summary>
public sealed class VocalSepCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    FFmpegConvertOperator ffmpegOp,
    VocalSeparationOperator vocalSepOp,
    ILogger<VocalSepCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "vocalsep";

    /// <inheritdoc />
    protected override bool AcceptsMedia => true;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config) => [ffmpegOp, vocalSepOp];

    /// <inheritdoc />
    protected override void ApplyMediaConfig(WorkflowConfig config, OperatorSettings settings)
    {
        config.VocalSeparation = true;
        config.VocalSeparationModel = "htdemucs";
    }
}

/// <summary>
/// <c>diarize</c> 独立算子命令：中间文件 → 只执行 说话人分割（crispasr / pyannote）。
/// </summary>
public sealed class DiarizeCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    PipelineOperatorFactory operatorFactory,
    ILogger<DiarizeCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "diarize";

    /// <inheritdoc />
    protected override bool AcceptsMedia => false;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config)
    {
        var diarizationOperator = operatorFactory.CreateDiarizationOperator(config);
        return diarizationOperator is null ? [] : [diarizationOperator];
    }
}

/// <summary>
/// <c>split</c> 独立算子命令：中间文件 → 只执行 分句。
/// </summary>
public sealed class SplitCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    PipelineOperatorFactory operatorFactory,
    ILogger<SplitCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "split";

    /// <inheritdoc />
    protected override bool AcceptsMedia => false;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config) =>
        [operatorFactory.CreateSentenceSplitOperator(config)];
}

/// <summary>
/// <c>clean</c> 独立算子命令：中间文件 → 只执行 文本清洗（标点/数字/缩写规范化）。
/// </summary>
public sealed class CleanCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    TextPreprocessingOperator textCleaningOp,
    ILogger<CleanCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "clean";

    /// <inheritdoc />
    protected override bool AcceptsMedia => false;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config) => [textCleaningOp];
}

/// <summary>
/// <c>align</c> 独立算子命令：中间文件 → 只执行 强制对齐（词级时间戳细化）。
/// </summary>
public sealed class AlignCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    PipelineOperatorFactory operatorFactory,
    ILogger<AlignCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "align";

    /// <inheritdoc />
    protected override bool AcceptsMedia => false;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config)
    {
        var alignmentOperator = operatorFactory.CreateAlignmentOperator(config);
        return alignmentOperator is null ? [] : [alignmentOperator];
    }
}

/// <summary>
/// <c>spellcheck</c> 独立算子命令：中间文件 → 只执行 拼写检查（Hunspell，输出 .spellcheck.json 报告）。
/// </summary>
public sealed class SpellCheckCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    SpellCheckOperator spellCheckOp,
    ILogger<SpellCheckCommand> logger,
    ICenturionDocumentStore store)
    : OperatorCommandBase(tempManager, pipelineExecutor, logger, store)
{
    /// <inheritdoc />
    protected override string OpName => "spellcheck";

    /// <inheritdoc />
    protected override bool AcceptsMedia => false;

    /// <inheritdoc />
    protected override IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config) => [spellCheckOp];
}

