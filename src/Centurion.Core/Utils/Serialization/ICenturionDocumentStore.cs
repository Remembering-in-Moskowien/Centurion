using Centurion.Models.Schema;

namespace Centurion.Core.Utils.Serialization;

/// <summary>Result of validating an IR file.</summary>
public sealed class DocumentValidationResult
{
    /// <summary>Whether validation passed (valid JSON + supported version + required structure present).</summary>
    public bool IsValid { get; init; }

    /// <summary>The schema version declared by the file (legacy format uses the marker "0").</summary>
    public string? Version { get; init; }

    /// <summary>The list of issues found during validation (empty means no issues).</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>Convenience factory for a success result.</summary>
    public static DocumentValidationResult Ok(string version) =>
        new() { IsValid = true, Version = version, Issues = [] };

    /// <summary>Convenience factory for a failure result.</summary>
    public static DocumentValidationResult Fail(string? version, params string[] issues) =>
        new() { IsValid = false, Version = version, Issues = issues };
}

/// <summary>
/// The unified read/write entry point for Centurion intermediate files (*.centurion.json): every
/// save/load/validate/migrate of an IR must go through this interface, keeping the format contract
/// in a single place.
/// </summary>
public interface ICenturionDocumentStore
{
    /// <summary>
    /// Reads an intermediate file and restores it to a <see cref="CenturionDocument"/>.
    /// Stays compatible with the legacy meta/config/state format (auto-detected as legacy, no
    /// exception); any other invalid content throws <see cref="InvalidDataException"/> (corrupt
    /// JSON / missing config/state / unsupported version).
    /// </summary>
    Task<CenturionDocument> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Serializes the document (System.Text.Json source generator) and writes it to the given path.</summary>
    Task<string> SaveAsync(CenturionDocument document, string path, CancellationToken cancellationToken);

    /// <summary>
    /// Validates whether an intermediate file is well-formed: JSON parseable, version supported,
    /// config/state structure present. It does not throw; issues are collected in
    /// <see cref="DocumentValidationResult.Issues"/>.
    /// </summary>
    Task<DocumentValidationResult> ValidateAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Migrates an intermediate file to the given version (currently only legacy → "1.0" is
    /// supported). Returns the migrated document (not written to disk); returns it as-is when it
    /// is already at the target version. Throws <see cref="NotSupportedException"/> for an
    /// unsupported migration target.
    /// </summary>
    Task<CenturionDocument> MigrateAsync(string path, string toVersion, CancellationToken cancellationToken);
}
