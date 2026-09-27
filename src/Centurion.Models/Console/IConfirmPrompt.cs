namespace Centurion.Models.Console;

/// <summary>用户确认提示端口，用于在命令行向用户展示确认问题并等待其答复。</summary>
public interface IConfirmPrompt
{
    /// <summary>异步向用户展示确认提示并获取其是否同意。</summary>
    /// <param name="prompt">向用户展示的提示文案。</param>
    /// <returns>用户确认返回 true，拒绝返回 false。</returns>
    Task<bool> ConfirmAsync(string prompt);
}