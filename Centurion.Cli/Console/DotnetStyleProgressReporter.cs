using Centurion.Models.Console;
namespace Centurion.Cli.Console;

/// <summary>
/// dotnet CLI 风格的单行 spinner 动画进度（替代复杂的 Spectre 复合进度条）。
/// 以一行旋转字符 + 文本描述实时刷新，完成时清行，风格与 dotnet 构建输出一致、克制。
/// </summary>
public sealed class DotnetStyleProgressReporter : IProgressReporter
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    /// <summary>
    /// 启动单行 spinner 进度，执行给定动作后停止并清行。
    /// </summary>
    /// <param name="title">spinner 前缀标题文本。</param>
    /// <param name="action">在 spinner 运行期间执行的进度动作，可通过上下文更新进度。</param>
    public void StartProgress(string title, Action<IProgressContext> action)
    {
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
    // Spinner 状态：后台线程按固定间隔旋转刷新当前行
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

            // 清空 spinner 行，避免残留动画字符
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
    // 上下文 / 任务适配（兼容现有 IProgressReporter 用法）
    // ------------------------------------------------------------------

    private sealed class SpinnerProgressContext(SpinnerState spinner) : IProgressContext
    {
        public IProgressTask AddTask(string description, long maxValue = 100)
            => new SpinnerProgressTask(spinner, description, maxValue);

        public void Refresh()
        {
            // 单行刷新由 spinner 线程持续进行，无需额外操作
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
