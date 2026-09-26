using System.Diagnostics.CodeAnalysis;

namespace Centurion.Models.Console;

/// <summary>
/// CLI 符号表：Unicode 终端（Windows Terminal/现代终端）使用 ✔ ▶ ● 等装饰符号；
/// 旧 Windows conhost（GBK 代码页/缺字体）下自动降级为 ASCII，避免"不可识别符号"。
/// 由 CLI 入口在启动时按终端能力调用 <see cref="Initialize"/>。
/// </summary>
public static class CliSymbols
{
    /// <summary>成功徽章。</summary>
    [NotNull] public static string Check = "✔";
    /// <summary>失败/错误符号。</summary>
    [NotNull] public static string Cross = "✖";
    /// <summary>步骤箭头。</summary>
    [NotNull] public static string Arrow = "→";
    /// <summary>开始/执行符号。</summary>
    [NotNull] public static string Play = "▶";
    /// <summary>完成符号。</summary>
    [NotNull] public static string Done = "✓";
    /// <summary>就绪圆点。</summary>
    [NotNull] public static string Dot = "●";
    /// <summary>未就绪空心圆。</summary>
    [NotNull] public static string Ring = "○";
    /// <summary>间隔点。</summary>
    [NotNull] public static string MidDot = "·";

    /// <summary>当前终端是否支持 Unicode 装饰符号。</summary>
    public static bool UnicodeSafe { get; private set; } = true;

    /// <summary>按终端能力切换符号集（ASCII 降级）。</summary>
    public static void Initialize(bool unicodeSafe)
    {
        UnicodeSafe = unicodeSafe;
        if (unicodeSafe)
            return;
        Check = "[OK]";
        Cross = "[ERR]";
        Arrow = "->";
        Play = ">";
        Done = "ok";
        Dot = "*";
        Ring = "o";
        MidDot = "-";
    }
}
