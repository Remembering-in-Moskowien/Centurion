using System.Text;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

public sealed class ScriptLoaderOp(ILogger<ScriptLoaderOp> logger) : PipelineOperatorBase
{
    public override string Name => "Script Loading";

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

        context.State.ScriptSentences = sentences;
        context.State.SplitSentences = sentences;
        logger.LogInformation("Loaded {Count} script lines from {Path}.", sentences.Count, path);
        OnProgress(100, $"Loaded {sentences.Count} script lines.");
    }
}