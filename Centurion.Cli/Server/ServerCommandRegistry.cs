using Centurion.Cli.Commands;
using Centurion.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace Centurion.Cli.Server;

/// <summary>
/// Executable command registry: command name → (Settings type, Command type).
/// The REST endpoint <c>POST /commands/{name}</c> instantiates and runs commands
/// from this registry; register new packaged commands here to expose them.
/// </summary>
public static class ServerCommandRegistry
{
    /// <summary>Command name → (Settings type, Command type).</summary>
    public static readonly Dictionary<string, (Type SettingsType, Type CommandType)> Commands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn"] = (typeof(SpawnSettings), typeof(SpawnCommand)),
            ["from-script"] = (typeof(FromScriptSettings), typeof(FromScriptCommand)),
            ["correct"] = (typeof(CorrectSettings), typeof(CorrectCommand)),
            ["translate"] = (typeof(TranslateSettings), typeof(TranslateCommand)),
            ["dub"] = (typeof(DubSettings), typeof(DubCommand)),
            ["convert"] = (typeof(ConvertSettings), typeof(ConvertCommand)),
            ["build"] = (typeof(BuildSettings), typeof(BuildCommand))
        };

    /// <summary>Registered command names (sorted).</summary>
    public static string[] Names => Commands.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Resolves a command entry by name; null for unknown commands.</summary>
    /// <param name="name">The command name.</param>
    /// <returns>The command entry (Settings/Command types), or null when unknown.</returns>
    public static (Type SettingsType, Type CommandType)? Resolve(string name) =>
        Commands.TryGetValue(name, out var entry) ? entry : null;
}
