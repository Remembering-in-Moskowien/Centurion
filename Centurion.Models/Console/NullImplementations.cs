// Centurion.Core/Infrastructure/NullImplementations.cs

using Centurion.Models.Console;

namespace Centurion.Core.Infrastructure;

/// <summary>
/// 控制台端口的空实现（Null Object 模式）。
/// 在未注入真实实现时提供无害的默认行为，避免空引用。
/// </summary>
public class NullConsoleOutput : IConsoleOutput
{
    public void Write(string message)
    {
    }

    public void WriteLine(string message)
    {
    }

    public void WriteError(string message)
    {
    }

    public void WriteWarning(string message)
    {
    }

    public void WriteSuccess(string message)
    {
    }

    public void WriteInfo(string message)
    {
    }

    public void WriteMarkup(string markup)
    {
    }

    public void WriteMarkupLine(string markup)
    {
    }
}

public class NullProgressReporter : IProgressReporter
{
    public void StartProgress(string title, Action<IProgressContext> action)
    {
        // 直接执行，无 UI
        var context = new NullProgressContext();
        action(context);
    }
}

public class NullProgressContext : IProgressContext
{
    public IProgressTask AddTask(string description, long maxValue = 100)
    {
        return new NullProgressTask();
    }

    public void Refresh()
    {
    }
}

public class NullProgressTask : IProgressTask
{
    public void SetValue(long value)
    {
    }

    public void SetMaxValue(long maxValue)
    {
    }

    public void SetDescription(string description)
    {
    }

    public void Increment(long amount = 1)
    {
    }

    public void Dispose()
    {
    }
}

public class NullConfirmPrompt : IConfirmPrompt
{
    public Task<bool> ConfirmAsync(string prompt)
    {
        return Task.FromResult(true);
    }
}
