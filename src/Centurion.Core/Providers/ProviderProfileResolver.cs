using Centurion.Abstractions.Providers;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// 当前生效的 Provider profile（全局）。CLI 解析 --profile 后设置；
/// 默认 <see cref="ProviderProfile.Fast"/>（允许云端，本地可用时优先本地）。
/// 也支持环境变量 CENTURION_PROFILE 覆盖。
/// </summary>
public static class ProviderProfileResolver
{
    private static ProviderProfile _current = FromString(Environment.GetEnvironmentVariable("CENTURION_PROFILE"));

    /// <summary>当前生效的 profile。</summary>
    public static ProviderProfile Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>解析 profile 字符串；未知值回退 <see cref="ProviderProfile.Fast"/>。</summary>
    public static ProviderProfile FromString(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "offline" => ProviderProfile.Offline,
        "fast" => ProviderProfile.Fast,
        "quality" => ProviderProfile.Quality,
        "cheap" => ProviderProfile.Cheap,
        _ => ProviderProfile.Fast
    };

    /// <summary>profile 是否允许使用云端 Provider。</summary>
    public static bool AllowsCloud(ProviderProfile profile) => profile != ProviderProfile.Offline;
}
