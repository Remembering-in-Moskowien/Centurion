using Centurion.Cli.Commands.Settings;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>validate 命令的选项。</summary>
public sealed class ValidateSettings : GlobalCommandSettings
{
    /// <summary>待校验的中间文件路径（可多个）。</summary>
    [CommandArgument(0, "<files>")]
    public string[] Files { get; set; } = [];
}

/// <summary>
/// <c>validate</c> 命令：校验一个或多个 *.centurion.json 是否符合 IR 契约
/// （JSON 可解析、schemaVersion 受支持、config/state 结构齐全）。
/// 非法文件列出全部问题并以非零退出码结束。
/// </summary>
public sealed class ValidateCommand(ICenturionDocumentStore store) : AsyncCommand<ValidateSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ValidateSettings settings, CancellationToken ct)
    {
        if (settings.Files.Length == 0)
        {
            ConsoleServices.Output.WriteError(ConsoleServices.T("Usage: Centurion validate <file.centurion.json> [...]"));
            return 1;
        }

        var failed = 0;
        foreach (var file in settings.Files)
        {
            var result = await store.ValidateAsync(file, ct);
            if (result.IsValid)
            {
                ConsoleServices.Output.WriteSuccess(ConsoleServices.T("OK: {0} (schema {1})", file, result.Version ?? "unknown"));
            }
            else
            {
                failed++;
                ConsoleServices.Output.WriteError(ConsoleServices.T("INVALID: {0}", file));
                foreach (var issue in result.Issues)
                    ConsoleServices.Output.WriteError("  - " + issue);
            }
        }
        return failed == 0 ? 0 : 1;
    }
}
