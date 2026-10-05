using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Centurion.Models;
using Centurion.Models.Schema;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Serialization;

/// <summary>
/// Default implementation of <see cref="ICenturionDocumentStore"/>: reads/writes via the
/// System.Text.Json source generator (<see cref="CenturionJsonContext"/>, no reflection), and
/// recognizes and stays compatible with the legacy meta/config/state format.
/// </summary>
public sealed class CenturionDocumentStore : ICenturionDocumentStore
{
    private static readonly JsonSerializerOptions Options = CenturionJsonContext.Default.Options;

    /// <summary>Returns whether the root JSON is in the legacy format (has "meta" but no "schemaVersion").</summary>
    private static bool IsLegacy(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("meta", out _) &&
        !root.TryGetProperty("schemaVersion", out _);

    /// <inheritdoc />
    public async Task<CenturionDocument> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Centurion intermediate file not found: {path}", path);

        using var doc = await ParseJsonAsync(path, cancellationToken);
        return ParseDocument(doc, path);
    }

    /// <inheritdoc />
    public async Task<string> SaveAsync(CenturionDocument document, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonSerializer.Serialize(document, CenturionJsonContext.Default.CenturionDocument);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    /// <inheritdoc />
    public async Task<DocumentValidationResult> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return DocumentValidationResult.Fail(null, $"File not found: {path}");

        try
        {
            using var doc = await ParseJsonAsync(path, cancellationToken);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return DocumentValidationResult.Fail(null, "Root must be a JSON object.");

            // Legacy format: readable structurally is treated as valid (the migrate command
            // handles upgrading)
            if (IsLegacy(root))
            {
                if (!root.TryGetProperty("config", out var cfg) || cfg.ValueKind != JsonValueKind.Object)
                    return DocumentValidationResult.Fail("legacy", "Legacy file is missing 'config' object.");
                if (!root.TryGetProperty("state", out var st) || st.ValueKind != JsonValueKind.Object)
                    return DocumentValidationResult.Fail("legacy", "Legacy file is missing 'state' object.");
                return DocumentValidationResult.Ok("legacy");
            }

            if (!root.TryGetProperty("schemaVersion", out var versionElem))
                return DocumentValidationResult.Fail(null, "Missing 'schemaVersion' — not a Centurion intermediate file.");
            var version = versionElem.GetString();
            if (!CenturionSchema.IsSupported(version))
                return DocumentValidationResult.Fail(version,
                    $"Unsupported schema version '{version}' (supported: {CenturionSchema.CurrentVersion}). Run 'centurion migrate' to upgrade.");

            try
            {
                _ = ParseDocument(doc, path);
            }
            catch (InvalidDataException ex)
            {
                return DocumentValidationResult.Fail(version, ex.Message);
            }
            return DocumentValidationResult.Ok(version ?? "unknown");
        }
        catch (JsonException ex)
        {
            return DocumentValidationResult.Fail(null, $"Not valid JSON: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<CenturionDocument> MigrateAsync(string path, string toVersion, CancellationToken cancellationToken)
    {
        if (!string.Equals(toVersion, CenturionSchema.CurrentVersion, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"Migration to '{toVersion}' is not supported (current: {CenturionSchema.CurrentVersion}).");

        if (!File.Exists(path))
            throw new FileNotFoundException($"Centurion intermediate file not found: {path}", path);

        using var doc = await ParseJsonAsync(path, cancellationToken);
        var root = doc.RootElement;

        if (IsLegacy(root))
            return ConvertLegacy(root, path);

        // Already the new format: validate and return as-is
        var parsed = ParseDocument(doc, path);
        if (parsed.SchemaVersion == toVersion)
            return parsed;

        throw new NotSupportedException(
            $"File is at version '{parsed.SchemaVersion}'; no migration path to '{toVersion}'.");
    }

    /// <summary>Parses JSON text (strict mode: no trailing commas/comments allowed; throws JsonException on corruption).</summary>
    private static async Task<JsonDocument> ParseJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var opts = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        };
        return await JsonDocument.ParseAsync(stream, opts, cancellationToken);
    }

    /// <summary>Converts parsed JSON into a <see cref="CenturionDocument"/> (new format deserialized directly; legacy format goes through migration).</summary>
    private static CenturionDocument ParseDocument(JsonDocument doc, string path)
    {
        var root = doc.RootElement;
        if (IsLegacy(root))
            return ConvertLegacy(root, path);

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"'{path}' is not a valid Centurion intermediate file: root must be a JSON object.");
        if (!root.TryGetProperty("schemaVersion", out _))
            throw new InvalidDataException($"'{path}' is missing 'schemaVersion' — not a Centurion intermediate file.");

        CenturionDocument? document;
        try
        {
            document = root.Deserialize<CenturionDocument>(Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{path}' is not a valid Centurion intermediate file: {ex.Message}", ex);
        }

        if (document?.Config is null || document.State is null)
            throw new InvalidDataException($"'{path}' is missing config/state — not a Centurion intermediate file.");

        if (!CenturionSchema.IsSupported(document.SchemaVersion))
            throw new InvalidDataException(
                $"Unsupported Centurion file schemaVersion '{document.SchemaVersion}' (expected {CenturionSchema.CurrentVersion}). Run 'centurion migrate' to upgrade.");

        return document;
    }

    /// <summary>Legacy meta/config/state format → current <see cref="CenturionDocument"/> (in-memory migration; nothing written to disk).</summary>
    private static CenturionDocument ConvertLegacy(JsonElement root, string path)
    {
        WorkflowConfig config;
        WorkflowState state;
        try
        {
            config = root.GetProperty("config").Deserialize<WorkflowConfig>(Options)
                     ?? throw new InvalidDataException($"'{path}' is missing config — not a Centurion intermediate file.");
            state = root.GetProperty("state").Deserialize<WorkflowState>(Options)
                    ?? throw new InvalidDataException($"'{path}' is missing state — not a Centurion intermediate file.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{path}' legacy config/state cannot be parsed: {ex.Message}", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidDataException($"'{path}' is missing config/state — not a Centurion intermediate file.", ex);
        }

        var generator = new GeneratorInfo
        {
            Version = ToolVersion,
            Command = root.TryGetProperty("meta", out var meta)
                ? meta.TryGetProperty("command", out var cmd) ? cmd.GetString() : null
                : null,
            GeneratedAt = root.TryGetProperty("meta", out meta)
                ? meta.TryGetProperty("generatedAt", out var ts) ? ts.GetString() ?? DateTimeOffset.Now.ToString("O") : DateTimeOffset.Now.ToString("O")
                : DateTimeOffset.Now.ToString("O"),
            InputFile = root.TryGetProperty("meta", out meta)
                ? meta.TryGetProperty("inputFile", out var inp) ? inp.GetString() : null
                : null,
            OutputFile = root.TryGetProperty("meta", out meta)
                ? meta.TryGetProperty("outputFile", out var outp) ? outp.GetString() : null
                : null
        };

        return new CenturionDocument
        {
            SchemaVersion = CenturionSchema.CurrentVersion,
            Generator = generator,
            Provenance = [],
            Config = config,
            State = state
        };
    }

    /// <summary>Current tool version: uniformly the build number (build-N); falls back to the assembly InformationalVersion when missing.</summary>
    private static string ToolVersion => Centurion.Core.Utils.Infrastructure.BuildInfo.DisplayVersion;
}
