namespace Centurion.Models.Console;

/// <summary>Port for driving a task-based progress session in the command line.</summary>
public interface IProgressReporter
{
    /// <summary>Starts a progress session and creates and manages its context.</summary>
    /// <param name="title">Progress session title.</param>
    /// <param name="action">Work to perform in the session; update tasks through the supplied context.</param>
    void StartProgress(string title, Action<IProgressContext> action);
}

/// <summary>Progress session context that creates tasks and refreshes the display.</summary>
public interface IProgressContext
{
    /// <summary>Adds a task to the current progress session.</summary>
    /// <param name="description">Task description.</param>
    /// <param name="maxValue">Maximum task value; defaults to 100.</param>
    /// <returns>A handle for updating task progress.</returns>
    IProgressTask AddTask(string description, long maxValue = 100);
    /// <summary>Immediately refreshes the progress display.</summary>
    void Refresh();
}

/// <summary>Handle for one progress task; supports updating its value, maximum, and description and is disposable.</summary>
public interface IProgressTask : IDisposable
{
    /// <summary>Sets the task's current value.</summary>
    /// <param name="value">Current progress value.</param>
    void SetValue(long value);
    /// <summary>Sets the task's maximum value.</summary>
    /// <param name="maxValue">Maximum progress value.</param>
    void SetMaxValue(long maxValue);
    /// <summary>Updates the task description.</summary>
    /// <param name="description">New task description.</param>
    void SetDescription(string description);
    /// <summary>Increments the current progress by the specified amount.</summary>
    /// <param name="amount">Amount to add; defaults to 1.</param>
    void Increment(long amount = 1);
}