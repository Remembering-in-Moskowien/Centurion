
namespace Centurion.Models.Console;

/// <summary>
/// No-op implementations of console ports (Null Object pattern).
/// Provide harmless defaults and avoid null references when no concrete adapter is injected.
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

/// <summary>No-op progress reporter that runs the supplied work synchronously without rendering UI.</summary>
public class NullProgressReporter : IProgressReporter
{
    /// <inheritdoc />
    public void StartProgress(string title, Action<IProgressContext> action)
    {
        // Run directly without a UI.
        var context = new NullProgressContext();
        action(context);
    }
}

/// <summary>No-op progress context that does not render UI.</summary>
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

/// <summary>No-op progress task handle.</summary>
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

/// <summary>No-op confirmation prompt that returns true automatically for non-interactive scenarios.</summary>
public class NullConfirmPrompt : IConfirmPrompt
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string prompt)
    {
        return Task.FromResult(true);
    }
}
