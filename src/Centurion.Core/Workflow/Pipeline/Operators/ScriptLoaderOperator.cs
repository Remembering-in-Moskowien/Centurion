using System.Text;
using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Script loading operator: reads text line by line from the configured script file, treats each line as one <see cref="Sentence"/>,
/// and writes them to the workflow state, serving the scripted workflow as the sole basis for subtitle segmentation.
/// </summary>
public sealed class ScriptLoaderOperator : PipelineOperatorBase<ScriptLoaderOperator>
{
    private readonly ILogger<ScriptLoaderOperator> _logger;

    /// <summary>Creates a script loading operator instance.</summary>
    /// <param name="logger">Logger that records script loading.</param>
    public ScriptLoaderOperator(ILogger<ScriptLoaderOperator> logger) : base(logger)
    {
        _logger = logger;
    }

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Script Loading";

    /// <summary>
    /// Reads the script file and splits it into sentences by non-empty lines, writing to workflow state.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing the script file path.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel the reading process.</param>
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
