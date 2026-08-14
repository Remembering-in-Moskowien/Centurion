// Centurion.Core/ConsoleServices.cs

using System.Diagnostics.CodeAnalysis;
using Centurion.Core.Abstractions;

namespace Centurion.Core;

public static class ConsoleServices
{
    [NotNull] public static IConsoleOutput Output { get; set; } = new NullConsoleOutput();
    [NotNull] public static IProgressReporter Progress { get; set; } = new NullProgressReporter();
    [NotNull] public static IConfirmPrompt Confirm { get; set; } = new NullConfirmPrompt();
}