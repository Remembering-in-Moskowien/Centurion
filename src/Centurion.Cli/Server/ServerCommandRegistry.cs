using Centurion.Cli.Commands;
using Centurion.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace Centurion.Cli.Server;

/// <summary>
/// Executable command registry: command name → (Settings type, Command type).
/// The REST endpoint <c>POST /commands/{name}</c> instantiates and runs commands
/// from this registry. Names mirror the CLI command tree (see
/// <see cref="CommandRegistrationExtensions.ConfigureCenturionCommands"/>):
/// every non-interactive CLI command is exposed under the same name, and branch
/// subcommands (models, providers) are flattened as <c>models-list</c> etc.
/// Intentionally excluded: <c>init</c> (interactive wizard; hangs without a TTY),
/// <c>serve</c> itself, <c>models-remove</c> (requires interactive confirmation),
/// and <c>update</c> (not registered in the CLI).
/// </summary>
public static class ServerCommandRegistry
{
    /// <summary>Command name → (Settings type, Command type).</summary>
    public static readonly Dictionary<string, (Type SettingsType, Type CommandType)> Commands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ─── Core subtitle workflows (CLI names) ───
            ["asr"] = (typeof(SpawnSettings), typeof(SpawnCommand)),
            ["spawn"] = (typeof(SpawnSettings), typeof(SpawnCommand)), // legacy alias for asr
            ["ocr"] = (typeof(OcrSettings), typeof(OcrCommand)),
            ["from-script"] = (typeof(FromScriptSettings), typeof(FromScriptCommand)),
            ["correct"] = (typeof(CorrectSettings), typeof(CorrectCommand)),
            ["translate"] = (typeof(TranslateSettings), typeof(TranslateCommand)),
            ["dub"] = (typeof(DubSettings), typeof(DubCommand)),
            ["convert"] = (typeof(ConvertSettings), typeof(ConvertCommand)),
            ["combine"] = (typeof(CombineSettings), typeof(CombineCommand)),
            ["build"] = (typeof(BuildSettings), typeof(BuildCommand)),
            // ─── Quality and utilities ───
            ["quality"] = (typeof(QualitySettings), typeof(QualityCommand)),
            ["pipeline-graph"] = (typeof(PipelineGraphSettings), typeof(PipelineGraphCommand)),
            ["validate"] = (typeof(ValidateSettings), typeof(ValidateCommand)),
            ["doctor"] = (typeof(DoctorSettings), typeof(DoctorCommand)),
            ["migrate"] = (typeof(MigrateSettings), typeof(MigrateCommand)),
            // ─── Model registry (flattened branch; remove is interactive, excluded) ───
            ["models-list"] = (typeof(ModelsSettings), typeof(ModelsListCommand)),
            ["models-install"] = (typeof(ModelsNameSettings), typeof(ModelsInstallCommand)),
            ["models-verify"] = (typeof(ModelsNameSettings), typeof(ModelsVerifyCommand)),
            // ─── Provider inspection (flattened branch) ───
            ["providers-list"] = (typeof(ProvidersSettings), typeof(ProvidersListCommand)),
            ["providers-test"] = (typeof(ProvidersTestSettings), typeof(ProvidersTestCommand))
        };

    /// <summary>Registered command names (sorted).</summary>
    public static string[] Names => Commands.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Resolves a command entry by name; null for unknown commands.</summary>
    /// <param name="name">The command name.</param>
    /// <returns>The command entry (Settings/Command types), or null when unknown.</returns>
    public static (Type SettingsType, Type CommandType)? Resolve(string name) =>
        Commands.TryGetValue(name, out var entry) ? entry : null;
}
