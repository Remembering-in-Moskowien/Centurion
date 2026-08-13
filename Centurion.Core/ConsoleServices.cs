// Centurion.Core/ConsoleServices.cs
using Centurion.Core.Abstractions;

namespace Centurion.Core;

public static class ConsoleServices
{
    public static IConsoleOutput Output { get; set; } = new NullConsoleOutput();
    public static IProgressReporter Progress { get; set; } = new NullProgressReporter();
    public static IConfirmPrompt Confirm { get; set; } = new NullConfirmPrompt();
}