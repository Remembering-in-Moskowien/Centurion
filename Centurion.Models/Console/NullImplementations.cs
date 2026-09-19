using Centurion.Models.Console;

namespace Centurion.Core.Infrastructure;

/// <summary>
/// 控制台端口的空实现（Null Object 模式）。
/// 在未注入真实实现时提供无害的默认行为，避免空引用。
/// </summary>
public class NullConsoleOutput : IConsoleOutput
{
    /// <inheritdoc />
    public void Write(string message)
    {
    }

    /// <inheritdoc />
    public void WriteLine(string message)
    {
    }

    /// <inheritdoc />
    public void WriteError(string message)
    {
    }

    /// <inheritdoc />
    public void WriteWarning(string message)
    {
    }

    /// <inheritdoc />
    public void WriteSuccess(string message)
    {
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
    {
    }

    /// <inheritdoc />
    public void WriteMarkup(string markup)
    {
    }

    /// <inheritdoc />
    public void WriteMarkupLine(string markup)
    {
    }
}

/// <summary>不渲染任何 UI 的空进度报告器，仅同步执行传入的工作。</summary>
public class NullProgressReporter : IProgressReporter
{
    /// <inheritdoc />
    public void StartProgress(string title, Action<IProgressContext> action)
    {
        // 直接执行，无 UI
        var context = new NullProgressContext();
        action(context);
    }
}

/// <summary>不渲染任何 UI 的空进度上下文。</summary>
public class NullProgressContext : IProgressContext
{
    /// <inheritdoc />
    public IProgressTask AddTask(string description, long maxValue = 100)
    {
        return new NullProgressTask();
    }

    /// <inheritdoc />
    public void Refresh()
    {
    }
}

/// <summary>不执行任何操作的空进度任务句柄。</summary>
public class NullProgressTask : IProgressTask
{
    /// <inheritdoc />
    public void SetValue(long value)
    {
    }

    /// <inheritdoc />
    public void SetMaxValue(long maxValue)
    {
    }

    /// <inheritdoc />
    public void SetDescription(string description)
    {
    }

    /// <inheritdoc />
    public void Increment(long amount = 1)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>自动返回 true 的空确认提示实现，用于无需交互的场景。</summary>
public class NullConfirmPrompt : IConfirmPrompt
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string prompt)
    {
        return Task.FromResult(true);
    }
}
