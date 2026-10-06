using Centurion.Models.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// Serializes the subtitle workflow context (config + per-stage state + diagnostics) into a
/// rich-context JSON file for debugging/retrospective analysis, downstream processing, and
/// automation tooling.
/// </summary>
public static class WorkflowContextDumper
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new StringEnumConverter()]
    };

    /// <summary>
    /// Writes the workflow context together with runtime metadata into a .context.json file sharing the base name of <paramref name="assOutputPath"/>.
    /// </summary>
    /// <param name="context">Subtitle workflow context, including configuration and per-stage runtime state.</param>
    /// <param name="commandName">Name of the subcommand that triggered this run, such as "spawn".</param>
    /// <param name="assOutputPath">Path of the ASS subtitle file produced this run; determines where the JSON file is written and what it is named.</param>
    /// <param name="cancellationToken">Token used to cancel writing the file.</param>
    /// <returns>The full path of the written JSON file.</returns>
    public static async Task<string> WriteAsync(
        SubtitleWorkflowContext context,
        string commandName,
        string assOutputPath,
        CancellationToken cancellationToken)
    {
        var dumpPath = Path.ChangeExtension(assOutputPath, ".context.json");
        var json = BuildJson(context, commandName, assOutputPath);
        await File.WriteAllTextAsync(dumpPath, json, cancellationToken);
        return dumpPath;
    }

    /// <summary>
    /// Builds the rich-context JSON string (internal for unit testing).
    /// Structure: meta (command/timing/input-output) + config (full workflow configuration) + state (per-stage sentences, flags and diagnostics).
    /// </summary>
    /// <param name="context">Subtitle workflow context.</param>
    /// <param name="commandName">Name of the subcommand that triggered this run.</param>
    /// <param name="assOutputPath">Path of the ASS subtitle file produced this run.</param>
    /// <returns>The formatted JSON string.</returns>
    internal static string BuildJson(SubtitleWorkflowContext context, string commandName, string assOutputPath)
    {
        var payload = new
        {
            Meta = new
            {
                Command = commandName,
                GeneratedAt = DateTimeOffset.Now.ToString("O"),
                InputFile = context.Config.InputFilePath,
                OutputFile = assOutputPath
            },
            Config = context.Config,
            State = context.State
        };

        return JsonConvert.SerializeObject(payload, Settings);
    }
}
