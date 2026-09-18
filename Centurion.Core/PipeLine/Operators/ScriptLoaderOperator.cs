using System.Text;
using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

public sealed class ScriptLoaderOperator : PipelineOperatorBase<ScriptLoaderOperator>
{
    private readonly ILogger<ScriptLoaderOperator> _logger;

    public ScriptLoaderOperator(ILogger<ScriptLoaderOperator> logger) : base(logger)
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
        if (string.IsNullOrWhiteSpace(context.Config.SubtitleFilePath))
            context.State.CurrentSentences = sentences;
        _logger.LogInformation("Loaded {Count} script lines from {Path}.", sentences.Count, path);
        OnProgress(100, $"Loaded {sentences.Count} script lines.");
    }
}
