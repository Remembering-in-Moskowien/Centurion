using Centurion.Models.Console;
namespace Centurion.Cli.Console;

/// <summary>
/// dotnet-CLI-style single-line spinner progress (a restrained alternative to the
/// complex Spectre composite progress bar). Refreshes one line of spinner + text,
/// clears on completion, matching dotnet build output in style.
/// </summary>
public sealed class DotnetStyleProgressReporter : IProgressReporter
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    /// <summary>
    /// Starts single-line spinner progress, runs the action, then stops and clears the line.
    /// </summary>
    /// <param name="title">Spinner prefix title text.</param>
    /// <param name="action">Progress action run while the spinner is active; can update progress via the context.</param>
    public void StartProgress(string title, Action<IProgressContext> action)
    {
        // --agent mode: run the work synchronously without any spinner UI (LLM-friendly output stays clean).
        if (SpectreConsoleOutput.AgentMode)
        {
            action(new NullProgressContext());
            return;
        }

        var spinner = new SpinnerState(title);
        spinner.Start();
        try
        {
            action(new SpinnerProgressContext(spinner));
        }
        finally
        {
            spinner.Stop();
        }
    }

    // ------------------------------------------------------------------
    // Spinner state: a background thread refreshes the current line at a fixed interval.
    // ------------------------------------------------------------------

    private sealed class SpinnerState(string title)
    {
        private readonly Lock _lock = new();
        private Thread? _thread;
        private volatile bool _running;
        private volatile string _description = "";
        private volatile int _frame;

        public void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
                _thread = new Thread(Run) { IsBackground = true, Name = "centurion-spinner" };
                _thread.Start();
            }
        }

        public void Update(string description)
        {
            _description = description;
        }

        public void Stop()
        {
            lock (_lock)
            {
                _running = false;
                _thread = null;
            }

            // Clear the spinner line to avoid leaving animation characters behind.
            var width = 80;
            try { width = System.Console.WindowWidth; } catch (IOException) { } catch (ArgumentOutOfRangeException) { }
            if (width <= 0) width = 80;
            System.Console.Write("\r" + new string(' ', width) + "\r");
        }

        private void Run()
        {
            while (_running)
            {
                var frame = Frames[_frame % Frames.Length];
                _frame++;
                var text = string.IsNullOrEmpty(_description) ? title : $"{title} {_description}";
                System.Console.Write($"\r{frame} {text}");
                Thread.Sleep(100);
            }
        }
    }

    // ------------------------------------------------------------------
    // Context/task adapter for the existing IProgressReporter contract.
    // ------------------------------------------------------------------

    private sealed class SpinnerProgressContext(SpinnerState spinner) : IProgressContext
    {
        public IProgressTask AddTask(string description, long maxValue = 100)
            => new SpinnerProgressTask(spinner, description, maxValue);

        public void Refresh()
        {
            // The spinner thread refreshes the single line continuously.
        }
    }

    private sealed class SpinnerProgressTask(SpinnerState spinner, string description, long maxValue) : IProgressTask
    {
        private long _value;
        private string _description = description;
        private long _max = maxValue;

        public void SetValue(long value)
        {
            _value = value;
            Render();
        }

        public void SetMaxValue(long maxValue)
        {
            _max = maxValue;
            Render();
        }

        public void SetDescription(string description)
        {
            _description = description;
            Render();
        }

        public void Increment(long amount = 1)
        {
            _value += amount;
            Render();
        }

        public void Dispose()
        {
        }

        private void Render()
        {
            if (_max > 0)
            {
                var pct = Math.Clamp((int)(_value * 100 / _max), 0, 100);
                spinner.Update($"{_description} [{pct}%] {FormatBytes(_value)} / {FormatBytes(_max)}");
            }
            else
            {
                spinner.Update($"{_description} {FormatBytes(_value)}");
            }
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):F2} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):F2} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):F2} KB",
        _ => $"{bytes} B"
    };
}
