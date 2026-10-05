using Spectre.Console;

namespace Centurion.Cli;

/// <summary>
/// CLI layout constants and helpers: unified table width (title-centered, columns
/// sized by content without wrapping). Width = terminal width - 4, clamped to [60, 120].
/// </summary>
public static class CliLayout
{
    private static bool DetectUnicode()
    {
        if (!OperatingSystem.IsWindows())
            return true;
        // Modern terminal indicators: Windows Terminal, VS Code, ConEmu, Hyper, and similar terminals.
        foreach (var v in new[] { "WT_SESSION", "TERM_PROGRAM", "ConEmuANSI", "VSCODE_PID" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 })
                return true;
        // Legacy conhost may lack Unicode support, so fall back to ASCII for readability.
        return false;
    }

    /// <summary>Whether the terminal supports Unicode decorative symbols (Windows Terminal/modern terminals true; legacy conhost degrades to ASCII).</summary>
    public static bool UnicodeSafe { get; } = DetectUnicode();

    /// <summary>Unified table border: rounded in Unicode terminals, ASCII in legacy terminals to avoid mojibake.</summary>
    public static TableBorder Border => UnicodeSafe ? TableBorder.Rounded : TableBorder.Ascii;

    /// <summary>Preferred total width for main tables (96 when redirected or width is unavailable).</summary>
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
