using Centurion.Cli.Commands;
using Centurion.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace Centurion.Server.Commands;

/// <summary>
/// 可执行命令注册表：命令名 →（Settings 类型, Command 类型）。
/// REST 端点 <c>POST /commands/{name}</c> 按此注册表实例化命令并执行；
/// 新增打包命令时在此登记即可对外暴露。
/// </summary>
public static class ServerCommandRegistry
{
    /// <summary>命令名 →（Settings 类型, Command 类型）。</summary>
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

    /// <summary>已登记的命令名列表（排序）。</summary>
    public static string[] Names => Commands.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>按名称解析命令项；未知命令返回 null。</summary>
    /// <param name="name">命令名。</param>
    /// <returns>命令项（Settings/Command 类型），未知时为 null。</returns>
    public static (Type SettingsType, Type CommandType)? Resolve(string name) =>
        Commands.TryGetValue(name, out var entry) ? entry : null;
}
