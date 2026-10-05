using System.Reflection;

namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// Build identity: unified "version → build number". Reads the build sequence number
/// from build-number.txt in the output directory (shared source for the banner Build #N and
/// IR provenance generator.version); falls back to the assembly InformationalVersion
/// (semantic version + commit), then to the assembly version.
/// </summary>
public static class BuildInfo
{
    /// <summary>Build number (e.g. 75); null when no build file exists or reading fails.</summary>
    public static string? BuildNumber { get; } = ReadBuildNumber();

    /// <summary>Display version label: prefers build-N, falls back to the semantic version when missing (keeps old artifacts readable).</summary>
    public static string DisplayVersion =>
        BuildNumber is { Length: > 0 } number ? $"build-{number}" : AssemblyVersion;

    private static string AssemblyVersion =>
        typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(BuildInfo).Assembly.GetName().Version?.ToString() ?? "unknown";

    private static string? ReadBuildNumber()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "build-number.txt");
            if (!File.Exists(file))
                return null;
            var raw = File.ReadAllText(file).Trim();
            return raw.Length > 0 ? raw : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
