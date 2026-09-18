// Centurion.Core/Infrastructure/ConsoleServices.cs

using System.Diagnostics.CodeAnalysis;
using Centurion.Core.Abstractions.Console;

namespace Centurion.Core.Infrastructure;

/// <summary>
/// 控制台输出端口门面（Facade）。
/// 以静态方式向非 DI 场景（模型/工具/算子）提供统一的控制台输出、进度与确认能力，
/// 具体实现由 CLI 入口在启动时注入（Adapter 模式）。
/// </summary>
public static class ConsoleServices
{
    [NotNull] public static IConsoleOutput Output { get; set; } = new NullConsoleOutput();
    [NotNull] public static IProgressReporter Progress { get; set; } = new NullProgressReporter();
    [NotNull] public static IConfirmPrompt Confirm { get; set; } = new NullConfirmPrompt();
}
