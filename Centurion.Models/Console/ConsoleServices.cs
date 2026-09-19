using System.Diagnostics.CodeAnalysis;
using Centurion.Models.Console;

namespace Centurion.Core.Infrastructure;

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
}
