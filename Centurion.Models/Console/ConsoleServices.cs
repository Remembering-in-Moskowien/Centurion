using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Centurion.Models.Console;

/// <summary>
/// 控制台输出端口门面（Facade）。
/// 以静态方式向非 DI 场景（模型/工具/算子）提供统一的控制台输出、进度与确认能力，
/// 具体实现由 CLI 入口在启动时注入（Adapter 模式）。
/// </summary>
public static class ConsoleServices
{
    /// <summary>控制台文本输出端口，未注入时默认为空实现。</summary>
    [NotNull] public static IConsoleOutput Output { get; set; } = new NullConsoleOutput();
    /// <summary>进度展示端口，未注入时默认为空实现。</summary>
    [NotNull] public static IProgressReporter Progress { get; set; } = new NullProgressReporter();
    /// <summary>用户确认提示端口，未注入时默认为自动确认的空实现。</summary>
    [NotNull] public static IConfirmPrompt Confirm { get; set; } = new NullConfirmPrompt();

    /// <summary>JSON 本地化器，由 CLI 入口在启动时注入；为 null 时消息按英文原文输出。</summary>
    public static IStringLocalizer? Localizer { get; set; }

    /// <summary>
    /// 取本地化文本：key 即英文默认文本，按当前语言在 JSON 资源中查找翻译；
    /// 未配置本地化器或未命中时回退 key（含占位符格式化）。
    /// </summary>
    /// <param name="key">消息 key（英文默认文本，可含 {0} 等占位符）。</param>
    /// <param name="args">占位符参数。</param>
    /// <returns>本地化后的文本。</returns>
    public static string T(string key, params object?[] args)
    {
        if (Localizer is null)
            return args.Length == 0 ? key : string.Format(CultureInfo.InvariantCulture, key, args);

        var localized = args.Length == 0 ? Localizer[key] : Localizer[key, (object[])args];
        if (!localized.ResourceNotFound)
            return localized.Value;

        return args.Length == 0 ? key : string.Format(CultureInfo.InvariantCulture, key, args);
    }
}
