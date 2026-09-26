using Spectre.Console;
using Centurion.Models.Console;
namespace Centurion.Cli.Console;

/// <summary>
/// 基于 Spectre.Console 的交互式确认提示实现。
/// </summary>
public class SpectreConfirmPrompt : IConfirmPrompt
{
    /// <summary>
    /// 向用户展示确认提示并返回其选择结果。
    /// </summary>
    /// <param name="prompt">向用户展示的提示文本。</param>
    /// <returns>用户确认返回 true，取消返回 false。</returns>
    public Task<bool> ConfirmAsync(string prompt)
    {
        return Task.FromResult(AnsiConsole.Confirm(prompt));
    }
}