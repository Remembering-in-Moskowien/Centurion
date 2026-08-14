// Centurion.Cli.Console/SpectreProgressReporter.cs

using Centurion.Core.Abstractions;
using Spectre.Console;

namespace Centurion.Cli.Console;

public class SpectreProgressReporter : IProgressReporter
{
    public void StartProgress(string title, Action<IProgressContext> action)
    {
        AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new TransferSpeedColumn(),
                new RemainingTimeColumn()
            })
            .Start(ctx =>
            {
                var wrapper = new SpectreProgressContext(ctx);
                action(wrapper);
            });
    }
}

internal class SpectreProgressContext(ProgressContext context) : IProgressContext
{
    public IProgressTask AddTask(string description, long maxValue = 100)
    {
        var task = context.AddTask(description, maxValue: maxValue);
        return new SpectreProgressTask(task);
    }

    public void Refresh()
    {
        context.Refresh();
    }
}

internal class SpectreProgressTask(ProgressTask task) : IProgressTask
{
    public void SetValue(long value)
    {
        task.Value = value;
    }

    public void SetMaxValue(long maxValue)
    {
        task.MaxValue = maxValue;
    }

    public void SetDescription(string description)
    {
        task.Description = description;
    }

    public void Increment(long amount = 1)
    {
        task.Increment(amount);
    }

    public void Dispose()
    {
        task.StopTask();
    }
}