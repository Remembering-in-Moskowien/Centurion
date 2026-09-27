using System.Reflection;

namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// 构建标识：统一"版本号 → 构建号"。从输出目录 build-number.txt 读取"第几号构建"
/// （横幅 Build #N 与 IR 溯源 generator.version 共用同一来源），缺失时回退程序集
/// InformationalVersion（语义版本 + commit），再回退程序集版本。
/// </summary>
public static class BuildInfo
{
    /// <summary>构建号（如 75）；无构建文件或读取失败时为 null。</summary>
    public static string? BuildNumber { get; } = ReadBuildNumber();

    /// <summary>展示用的版本标签：优先 build-N，缺失回退语义版本（保持旧产物可读）。</summary>
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
