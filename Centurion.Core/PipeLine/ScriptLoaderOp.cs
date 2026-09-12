using System.Text;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

public sealed class ScriptLoaderOp : PipelineOperatorBase<ScriptLoaderOp>
{
    private readonly ILogger<ScriptLoaderOp> _logger;

    public ScriptLoaderOp(ILogger<ScriptLoaderOp> logger) : base(logger)
    {
        _logger = logger;
    }

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

        if (sentences.Count == 0)
        {
            const string message = "The script file does not contain any non-empty lines.";
            context.State.Errors.Add(message);
            _logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        context.State.ScriptSentences = sentences;
        context.State.CurrentSentences = sentences;
        _logger.LogInformation("Loaded {Count} script lines from {Path}.", sentences.Count, path);
        OnProgress(100, $"Loaded {sentences.Count} script lines.");
    }
}