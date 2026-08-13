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

internal class SpectreProgressContext : IProgressContext
{
    private readonly ProgressContext _context;
    public SpectreProgressContext(ProgressContext context) => _context = context;
    public IProgressTask AddTask(string description, long maxValue = 100)
    {
        var task = _context.AddTask(description, maxValue: maxValue);
        return new SpectreProgressTask(task);
    }
    public void Refresh() => _context.Refresh();
}

internal class SpectreProgressTask : IProgressTask
{
    private readonly ProgressTask _task;
    public SpectreProgressTask(ProgressTask task) => _task = task;
    public void SetValue(long value) => _task.Value = value;
    public void SetMaxValue(long maxValue) => _task.MaxValue = maxValue;
    public void SetDescription(string description) => _task.Description = description;
    public void Increment(long amount = 1) => _task.Increment(amount);
    public void Dispose() => _task.StopTask();
}