namespace Centurion.Abstractions;

/// <summary>
/// Standardized CLI exit codes that scripts can use to determine the result.
/// </summary>
public static class ExitCodes
{
    /// <summary>Success.</summary>
    public const int Success = 0;

    /// <summary>Execution failed, for example because of a missing model, pipeline error, or I/O error.</summary>
    public const int Failure = 1;

    /// <summary>Usage error, such as an unknown command or argument or invalid configuration. Spectre handles its own parsing errors.</summary>
    public const int Usage = 2;

    /// <summary>Cancelled by the user (Ctrl+C).</summary>
    public const int Cancelled = 130;
}
