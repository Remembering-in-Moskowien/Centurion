using Spectre.Console;

namespace Centurion.Cli;

/// <summary>
/// CLI 排版常量与计算：统一表格总宽（标题居中与表格视觉一致，
/// 列按内容分配不折行）。宽度 = 终端宽 - 4，限幅 [60, 120]。
/// </summary>
public static class CliLayout
{
    private static bool DetectUnicode()
    {
        if (!OperatingSystem.IsWindows())
            return true;
        // 现代终端标志：Windows Terminal / VS Code / ConEmu / Hyper 等
        foreach (var v in new[] { "WT_SESSION", "TERM_PROGRAM", "ConEmuANSI", "VSCODE_PID" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 })
                return true;
        // 旧 conhost：GBK 代码页/缺字体 → 保守降级 ASCII，保证可读
        return false;
    }

    /// <summary>终端是否支持 Unicode 装饰符号（Windows Terminal/现代终端为 true；旧 conhost 降级 ASCII）。</summary>
    public static bool UnicodeSafe { get; } = DetectUnicode();

    /// <summary>统一表格边框：Unicode 终端用圆角，旧终端用 ASCII 边框避免乱码。</summary>
    public static TableBorder Border => UnicodeSafe ? TableBorder.Rounded : TableBorder.Ascii;

    /// <summary>主表格首选总宽（终端重定向/无宽度时按 96 处理）。</summary>
    public static int TableWidth()
    {
        try
        {
            var w = System.Console.WindowWidth;
            return Math.Clamp(w - 4, 60, 120);
        }
        catch (IOException)
        {
            return 96;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 96;
        }
    }
}
