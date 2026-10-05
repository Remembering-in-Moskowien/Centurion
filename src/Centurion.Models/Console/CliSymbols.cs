using System.Diagnostics.CodeAnalysis;

namespace Centurion.Models.Console;

/// <summary>
/// CLI symbol table: Unicode terminals (Windows Terminal and other modern terminals) use decorative symbols such as ✔ ▶ ●.
/// Legacy Windows conhost falls back to ASCII when the code page or fonts do not support these symbols.
/// The CLI entry point calls <see cref="Initialize"/> at startup based on terminal capabilities.
/// </summary>
public static class CliSymbols
{
    /// <summary>Success badge.</summary>
    [NotNull] public static string Check = "✓";
    /// <summary>Failure/error symbol.</summary>
    [NotNull] public static string Cross = "✖";
    /// <summary>Step arrow.</summary>
    [NotNull] public static string Arrow = "→";
    /// <summary>Start/execution symbol.</summary>
    [NotNull] public static string Play = "▶";
    /// <summary>Completion symbol.</summary>
    [NotNull] public static string Done = "✓";
    /// <summary>Ready-state dot.</summary>
    [NotNull] public static string Dot = "●";
    /// <summary>Not-ready hollow circle.</summary>
    [NotNull] public static string Ring = "○";
    /// <summary>Separator dot.</summary>
    [NotNull] public static string MidDot = "·";

    /// <summary>Whether the current terminal supports Unicode decorative symbols.</summary>
    public static bool UnicodeSafe { get; private set; } = true;

    /// <summary>Selects the symbol set based on terminal capabilities, falling back to ASCII when necessary.</summary>
    public static void Initialize(bool unicodeSafe)
    {
        UnicodeSafe = unicodeSafe;
        if (unicodeSafe)
            return;
        Check = "OK";      // Do not use brackets: Spectre parses [OK] as a color tag and throws.
        Cross = "ERR";
        Arrow = "->";
        Play = ">";
        Done = "ok";
        Dot = "*";
        Ring = "o";
        MidDot = "-";
    }
}
