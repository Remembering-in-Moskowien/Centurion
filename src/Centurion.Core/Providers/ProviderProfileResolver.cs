using Centurion.Abstractions.Providers;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// The currently active (global) provider profile. Set by the CLI after parsing --profile;
/// it defaults to <see cref="ProviderProfile.Fast"/> (allows cloud, preferring local when available).
/// The CENTURION_PROFILE environment variable can also override it.
/// </summary>
public static class ProviderProfileResolver
{
    private static ProviderProfile _current = FromString(Environment.GetEnvironmentVariable("CENTURION_PROFILE"));

    /// <summary>The currently active profile.</summary>
    public static ProviderProfile Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>Parses a profile string; unknown values fall back to <see cref="ProviderProfile.Fast"/>.</summary>
    public static ProviderProfile FromString(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "offline" => ProviderProfile.Offline,
        "fast" => ProviderProfile.Fast,
        "quality" => ProviderProfile.Quality,
        "cheap" => ProviderProfile.Cheap,
        _ => ProviderProfile.Fast
    };

    /// <summary>Whether the profile allows using cloud providers.</summary>
    public static bool AllowsCloud(ProviderProfile profile) => profile != ProviderProfile.Offline;
}
