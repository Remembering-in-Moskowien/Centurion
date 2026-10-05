namespace Centurion.Models.Console;

/// <summary>Console text output port abstracting command-line write operations.</summary>
public interface IConsoleOutput
{
    /// <summary>Writes text without a newline.</summary>
    /// <param name="message">Text to write.</param>
    void Write(string message);
    /// <summary>Writes a line of text.</summary>
    /// <param name="message">Text to write.</param>
    void WriteLine(string message);
    /// <summary>Writes an error line, usually styled in red.</summary>
    /// <param name="message">Error text to write.</param>
    void WriteError(string message);
    /// <summary>Writes a warning line, usually styled in yellow.</summary>
    /// <param name="message">Warning text to write.</param>
    void WriteWarning(string message);
    /// <summary>Writes a success line, usually styled in green.</summary>
    /// <param name="message">Success text to write.</param>
    void WriteSuccess(string message);
    /// <summary>Writes an information line in the standard style.</summary>
    /// <param name="message">Information text to write.</param>
    void WriteInfo(string message);
    /// <summary>Writes rich text with color/style markup without a newline.</summary>
    /// <param name="markup">Text containing style markup.</param>
    void WriteMarkup(string markup);
    /// <summary>Writes rich text with color/style markup and a newline.</summary>
    /// <param name="markup">Text containing style markup.</param>
    void WriteMarkupLine(string markup);
}