using System.Text;
using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 脚本加载算子：从配置的脚本文件逐行读取文本，每行作为一个 <see cref="Sentence"/>，
/// 写入工作流状态，供"带脚本"工作流作为字幕分段的唯一依据。
/// </summary>
public sealed class ScriptLoaderOperator : PipelineOperatorBase<ScriptLoaderOperator>
{
    private readonly ILogger<ScriptLoaderOperator> _logger;

    /// <summary>创建脚本加载算子实例。</summary>
    /// <param name="logger">记录脚本加载日志的记录器。</param>
    public ScriptLoaderOperator(ILogger<ScriptLoaderOperator> logger) : base(logger)
    {
        _logger = logger;
    }

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Script Loading";

    /// <summary>
    /// 读取脚本文件并按非空行切分为句子，写入工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供脚本文件路径。</param>
    /// <param name="cancellationToken">用于取消读取过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var path = context.Config.ScriptFilePath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("ScriptFilePath is required for the from-script workflow.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Script file not found: {path}", path);

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        var sentences = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => new Sentence { Text = line })
            .ToList();

        if (sentences.Count == 0)
        {
            const string message = "The script file does not contain any non-empty lines.";
            context.State.Errors.Add(message);
            _logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        context.State.ScriptSentences = sentences;
        if (string.IsNullOrWhiteSpace(context.Config.SubtitleFilePath))
            context.State.CurrentSentences = sentences;
        _logger.LogInformation("Loaded {Count} script lines from {Path}.", sentences.Count, path);
        OnProgress(100, $"Loaded {sentences.Count} script lines.");
    }
}
