namespace Centurion.Models.Console;

/// <summary>进度展示端口，用于在命令行驱动一个带任务的进度会话。</summary>
public interface IProgressReporter
{
    /// <summary>开始一个进度会话，内部会创建并管理进度上下文</summary>
    /// <param name="title">进度会话标题。</param>
    /// <param name="action">在该进度会话中执行的实际工作，通过回调传入的上下文更新任务。</param>
    void StartProgress(string title, Action<IProgressContext> action);
}

/// <summary>进度会话上下文，负责创建任务并刷新显示。</summary>
public interface IProgressContext
{
    /// <summary>在当前进度会话中新增一个任务。</summary>
    /// <param name="description">任务描述文案。</param>
    /// <param name="maxValue">任务的最大值（总量），默认为 100。</param>
    /// <returns>用于更新该任务进度的句柄。</returns>
    IProgressTask AddTask(string description, long maxValue = 100);
    /// <summary>立即刷新进度显示。</summary>
    void Refresh();
}

/// <summary>单个进度任务句柄，支持更新当前值、最大值、描述并随用毕释放。</summary>
public interface IProgressTask : IDisposable
{
    /// <summary>设置任务已完成的当前值。</summary>
    /// <param name="value">当前进度值。</param>
    void SetValue(long value);
    /// <summary>设置任务的最大值（总量）。</summary>
    /// <param name="maxValue">最大进度值。</param>
    void SetMaxValue(long maxValue);
    /// <summary>更新任务的描述文案。</summary>
    /// <param name="description">新的任务描述。</param>
    void SetDescription(string description);
    /// <summary>将当前进度累加指定增量。</summary>
    /// <param name="amount">要增加的数量，默认为 1。</param>
    void Increment(long amount = 1);
}