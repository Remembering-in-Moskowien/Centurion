namespace Centurion.Models.Console;

/// <summary>
/// Custom color palette for the CLI: one place to tune the colors of warning/error
/// and other semantic output. Spectre markup names drive direct console rendering;
/// ConsoleColor constants drive the log-channel formatter fallback.
/// </summary>
public static class CliPalette
{
    /// <summary>Spectre markup color for error messages (red, bold).</summary>
    public const string Error = "red bold";
    /// <summary>
    /// Spectre markup color for warning messages (gold, bold) — 金色而非终端/Shell 默认的
    /// 黄色（如 PowerShell 警告色），避免语义撞衫。
    /// </summary>
    public const string Warning = "gold bold";
    /// <summary>Spectre markup color for critical failures (magenta, bold).</summary>
    public const string Critical = "magenta bold";
    /// <summary>Spectre markup color for success messages (green).</summary>
    public const string Success = "green";
    /// <summary>Spectre markup color for plain info text (white).</summary>
    public const string Info = "white";
    /// <summary>Spectre markup color for dimmed/hint text (grey).</summary>
    public const string Dim = "grey";
    /// <summary>Spectre markup color for accent/emphasis (cyan).</summary>
    public const string Accent = "cyan";

    /// <summary>ConsoleColor fallback for error lines in the log-channel formatter.</summary>
    public const System.ConsoleColor ErrorColor = System.ConsoleColor.Red;
    /// <summary>
    /// ConsoleColor fallback for warning lines in the log-channel formatter.
    /// ConsoleColor 无金色枚举，取最接近的暗黄（暗金）；Spectre 渲染主通道使用真金 <see cref="Warning"/>。
    /// </summary>
    public const System.ConsoleColor WarningColor = System.ConsoleColor.DarkYellow;
    /// <summary>ConsoleColor fallback for critical lines (custom magenta).</summary>
    public const System.ConsoleColor CriticalColor = System.ConsoleColor.Magenta;
    /// <summary>ConsoleColor fallback for success lines.</summary>
    public const System.ConsoleColor SuccessColor = System.ConsoleColor.Green;
    /// <summary>ConsoleColor fallback for plain info lines.</summary>
    public const System.ConsoleColor InfoColor = System.ConsoleColor.White;
}
