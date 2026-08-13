// Centurion.Core/Abstractions/IProgressReporter.cs
namespace Centurion.Core.Abstractions;

public interface IProgressReporter
{
    /// <summary>开始一个进度会话，内部会创建并管理进度上下文</summary>
    void StartProgress(string title, Action<IProgressContext> action);
}

public interface IProgressContext
{
    IProgressTask AddTask(string description, long maxValue = 100);
    void Refresh();
}

public interface IProgressTask : IDisposable
{
    void SetValue(long value);
    void SetMaxValue(long maxValue);
    void SetDescription(string description);
    void Increment(long amount = 1);
}