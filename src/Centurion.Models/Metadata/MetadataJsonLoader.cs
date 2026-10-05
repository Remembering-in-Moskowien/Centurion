using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Centurion.Models.Metadata;

/// <summary>
/// Metadata load result created at startup, containing tool and model registries.
/// </summary>
public sealed class MetadataCatalog
{
    /// <summary>Loaded tool registry, indexed by tool name.</summary>
    public required ToolRegistry Tools { get; init; }
    /// <summary>Loaded model registry, indexed by model category.</summary>
    public required ModelRegistry Models { get; init; }
}

/// <summary>
/// Loads external metadata JSON configuration.
/// <para>
/// Resolution order:
/// 1. Explicit path passed to AddCenturionCore.
/// 2. The <c>CENTURION_METADATA_PATH</c> environment variable.
/// 3. Default candidates under the executable directory or current directory at config\metadata.json.
/// </para>
/// <para>
/// Loads the target JSON when present; otherwise, or when parsing fails, falls back to the built-in registries
/// (<see cref="ToolRegistry.Default"/> / <see cref="ModelRegistry.Default"/>) and attempts to write an editable seed JSON file to the default path.
/// </para>
/// </summary>
public static class MetadataJsonLoader
{
    /// <summary>Environment variable that overrides the metadata JSON path.</summary>
    public const string EnvVarName = "CENTURION_METADATA_PATH";

    private const string DefaultFileName = "metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object Sync = new();
    private static readonly Dictionary<string, MetadataCatalog> CatalogCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads metadata, falling back to built-in defaults and attempting to create a seed file when external JSON is missing or invalid.
    /// Results are cached by resolved path within the process so repeated AddCenturionCore calls (for example, in serve mode)
    /// do not repeat merges, file writes, or merge notifications.
    /// </summary>
    public static MetadataCatalog LoadOrDefault(string? explicitPath = null, ILogger? logger = null)
    {
        var path = ResolvePath(explicitPath);
        lock (Sync)
        {
            if (CatalogCache.TryGetValue(path, out var cached))
                return cached;
            var catalog = LoadOrDefaultCore(path, logger);
            CatalogCache[path] = catalog;
            return catalog;
        }
    }

    private static MetadataCatalog LoadOrDefaultCore(string path, ILogger? logger)
    {
        if (File.Exists(path))
        {
            try
            {
                var catalog = LoadFromFile(path);
                var merged = MergeMissingDefaults(catalog);
                if (merged.Changed)
                {
                    // Add new tools and models introduced by upgrades while preserving user-defined entries.
                    logger?.LogInformation("New default entries merged into metadata config.");
                    WriteCatalogFile(path, merged.Catalog, logger);
                }
                return merged.Catalog;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to load metadata config from '{MetadataPath}'.", path);
                logger?.LogWarning("Falling back to built-in default metadata.");
            }
        }
        else
        {
            WriteSeedFile(path, logger);
        }

        return new MetadataCatalog
        {
            Tools = ToolRegistry.Default,
            Models = ModelRegistry.Default
        };
    }

    /// <summary>
    /// Resolves the final metadata JSON path.
    /// </summary>
    public static string ResolvePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var env = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config", DefaultFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "config", DefaultFileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return candidates[0];
    }

    // ---------- Loading ----------

    private static MetadataCatalog LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<MetadataFileDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Metadata JSON is empty.");

        return new MetadataCatalog
        {
            Tools = BuildTools(dto.Tools),
            Models = BuildModels(dto.Models)
        };
    }

    private static ToolRegistry BuildTools(Dictionary<string, ToolMetaDto>? dtoTools)
    {
        var dict = new Dictionary<string, ToolMeta>(StringComparer.OrdinalIgnoreCase);
        if (dtoTools == null)
            return new ToolRegistry(dict);

        foreach (var (key, dto) in dtoTools)
        {
            if (dto == null) continue;
            var name = string.IsNullOrWhiteSpace(dto.ToolName) ? key : dto.ToolName!;
            dict[name] = new ToolMeta
            {
                ToolName = name,
                DownloadUrl = dto.DownloadUrl
                    ?? throw new InvalidOperationException($"Tool '{key}' is missing 'downloadUrl'."),
                ArchiveType = string.IsNullOrWhiteSpace(dto.ArchiveType) ? "zip" : dto.ArchiveType!,
                ExecutableRelativePath = dto.ExecutableRelativePath
                    ?? throw new InvalidOperationException($"Tool '{key}' is missing 'executableRelativePath'."),
                Version = dto.Version,
                ModelBaseUrl = dto.ModelBaseUrl,
                FileHash = dto.FileHash,
                Variants = BuildVariants(dto.Variants)
            };
        }
        return new ToolRegistry(dict);
    }

    private static Dictionary<string, ToolVariant>? BuildVariants(Dictionary<string, ToolVariantDto>? dtoVariants)
    {
        if (dtoVariants == null || dtoVariants.Count == 0)
            return null;

        var result = new Dictionary<string, ToolVariant>(StringComparer.OrdinalIgnoreCase);
        foreach (var (deviceKey, dto) in dtoVariants)
        {
            if (dto == null) continue;
            result[deviceKey] = new ToolVariant
            {
                DownloadUrl = dto.DownloadUrl,
                ArchiveType = dto.ArchiveType,
                ExecutableRelativePath = dto.ExecutableRelativePath,
                Description = dto.Description,
                FileHash = dto.FileHash
            };
        }
        return result;
    }

    private static ModelRegistry BuildModels(Dictionary<string, Dictionary<string, ModelMetaDto>>? dtoModels)
    {
        return new ModelRegistry(
            BuildModelDict(dtoModels, "whisper"),
            BuildModelDict(dtoModels, "fasterWhisper"),
            BuildModelDict(dtoModels, "qwen3Asr"),
            BuildModelDict(dtoModels, "qwen3ForcedAligner"),
            BuildModelDict(dtoModels, "diarization"),
            BuildModelDict(dtoModels, "bertOnnx"),
            BuildModelDict(dtoModels, "qwen3Tts"),
            BuildModelDict(dtoModels, "indextts"));
    }

    private static Dictionary<string, ModelMeta> BuildModelDict(
        Dictionary<string, Dictionary<string, ModelMetaDto>>? all, string category)
    {
        var result = new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase);
        if (all != null && all.TryGetValue(category, out var entries) && entries != null)
        {
            foreach (var (key, dto) in entries)
            {
                if (dto == null) continue;
                result[key] = ToModelMeta(key, dto);
            }
        }
        return result;
    }

    private static ModelMeta ToModelMeta(string key, ModelMetaDto dto)
    {
        return ParseDownloadType(dto.DownloadType) switch
        {
            ModelDownloadType.Directory => new ModelMeta(
                dto.DownloadUrl ?? throw new InvalidOperationException($"Model '{key}' is missing 'downloadUrl'."),
                dto.Files ?? throw new InvalidOperationException($"Model '{key}' (directory) requires a non-empty 'files' list."),
                dto.Subdirectory)
            {
                FileHash = dto.FileHash
            },
            ModelDownloadType.OnnxModelDirectory => new ModelMeta(
                dto.DownloadUrl ?? throw new InvalidOperationException($"Model '{key}' is missing 'downloadUrl'."),
                dto.Files ?? throw new InvalidOperationException($"Model '{key}' (onnx-directory) requires a non-empty 'files' list."),
                dto.OnnxModelType ?? throw new InvalidOperationException($"Model '{key}' (onnx-directory) requires 'onnxModelType'."),
                dto.Subdirectory)
            {
                FileHash = dto.FileHash
            },
            _ => new ModelMeta(
                dto.FileName ?? throw new InvalidOperationException($"Model '{key}' is missing 'fileName'."),
                dto.DownloadUrl ?? throw new InvalidOperationException($"Model '{key}' is missing 'downloadUrl'."))
            {
                FileHash = dto.FileHash
            }
        };
    }

    private static ModelDownloadType ParseDownloadType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "single-file" or "singlefile" => ModelDownloadType.SingleFile,
        "directory" => ModelDownloadType.Directory,
        "onnx-directory" or "onnx" => ModelDownloadType.OnnxModelDirectory,
        _ => throw new InvalidOperationException($"Unknown model download type: '{value}'.")
    };

    // ---------- Merge missing entries ----------

    /// <summary>
    /// Adds tool and model entries that exist in the built-in registries but are missing from local configuration,
    /// preserving user-defined entries while making new capabilities available after upgrades.
    /// </summary>
    /// <param name="catalog">Loaded local registries.</param>
    /// <returns>Whether entries were added and the merged registries.</returns>
    private static (bool Changed, MetadataCatalog Catalog) MergeMissingDefaults(MetadataCatalog catalog)
    {
        var changed = false;
        var tools = new Dictionary<string, ToolMeta>(catalog.Tools.Tools, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ToolRegistry.Default.Tools)
        {
            if (!tools.ContainsKey(key))
            {
                tools[key] = value;
                changed = true;
            }
        }

        var models = BuildMergedModels(catalog.Models, ref changed);
        return (changed, new MetadataCatalog
        {
            Tools = new ToolRegistry(tools),
            Models = models
        });
    }

    private static ModelRegistry BuildMergedModels(ModelRegistry local, ref bool changed)
    {
        var whisper = MergeModelDict(local.WhisperModels, ModelRegistry.Default.WhisperModels, ref changed);
        var faster = MergeModelDict(local.FasterWhisperModels, ModelRegistry.Default.FasterWhisperModels, ref changed);
        var qwen = MergeModelDict(local.Qwen3AsrModels, ModelRegistry.Default.Qwen3AsrModels, ref changed);
        var aligner = MergeModelDict(local.Qwen3ForcedAlignerModels, ModelRegistry.Default.Qwen3ForcedAlignerModels, ref changed);
        var diar = MergeModelDict(local.DiarizationModels, ModelRegistry.Default.DiarizationModels, ref changed);
        var bert = MergeModelDict(local.BertOnnxModels, ModelRegistry.Default.BertOnnxModels, ref changed);
        var tts = MergeModelDict(local.Qwen3TtsModels, ModelRegistry.Default.Qwen3TtsModels, ref changed);
        var indextts = MergeModelDict(local.IndexTtsModels, ModelRegistry.Default.IndexTtsModels, ref changed);
        return new ModelRegistry(whisper, faster, qwen, aligner, diar, bert, tts, indextts);
    }

    private static Dictionary<string, ModelMeta> MergeModelDict(
        IReadOnlyDictionary<string, ModelMeta> local,
        IReadOnlyDictionary<string, ModelMeta> defaults,
        ref bool changed)
    {
        var result = new Dictionary<string, ModelMeta>(local, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in defaults)
        {
            if (!result.ContainsKey(key))
            {
                result[key] = value;
                changed = true;
            }
        }
        return result;
    }

    /// <summary>Serializes the registries back to the configuration file.</summary>
    /// <param name="path">Destination JSON path.</param>
    /// <param name="catalog">Registries to write.</param>
    /// <param name="logger">Logger for non-fatal write errors.</param>
    private static void WriteCatalogFile(string path, MetadataCatalog catalog, ILogger? logger)
    {
        try
        {
            var seed = new MetadataFileDto
            {
                Tools = catalog.Tools.Tools.ToDictionary(
                    kv => kv.Key, kv => ToDto(kv.Value), StringComparer.OrdinalIgnoreCase),
                Models = new Dictionary<string, Dictionary<string, ModelMetaDto>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["whisper"] = ToDtoDict(catalog.Models.WhisperModels),
                    ["fasterWhisper"] = ToDtoDict(catalog.Models.FasterWhisperModels),
                    ["qwen3Asr"] = ToDtoDict(catalog.Models.Qwen3AsrModels),
                    ["qwen3ForcedAligner"] = ToDtoDict(catalog.Models.Qwen3ForcedAlignerModels),
                    ["diarization"] = ToDtoDict(catalog.Models.DiarizationModels),
                    ["bertOnnx"] = ToDtoDict(catalog.Models.BertOnnxModels),
                    ["qwen3Tts"] = ToDtoDict(catalog.Models.Qwen3TtsModels),
                    ["indextts"] = ToDtoDict(catalog.Models.IndexTtsModels)
                }
            };

            File.WriteAllText(path, JsonSerializer.Serialize(seed, JsonOptions));
        }
        catch (Exception ex)
        {
            // A failed write does not affect this run; the merge is already applied in memory.
            logger?.LogWarning(ex, "Could not persist merged metadata at '{MetadataPath}'.", path);
        }
    }

    // ---------- Seed file ----------

    private static void WriteSeedFile(string path, ILogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var seed = new MetadataFileDto
            {
                Tools = ToolRegistry.Default.Tools.ToDictionary(
                    kv => kv.Key, kv => ToDto(kv.Value), StringComparer.OrdinalIgnoreCase),
                Models = new Dictionary<string, Dictionary<string, ModelMetaDto>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["whisper"] = ToDtoDict(ModelRegistry.Default.WhisperModels),
                    ["fasterWhisper"] = ToDtoDict(ModelRegistry.Default.FasterWhisperModels),
                    ["qwen3Asr"] = ToDtoDict(ModelRegistry.Default.Qwen3AsrModels),
                    ["qwen3ForcedAligner"] = ToDtoDict(ModelRegistry.Default.Qwen3ForcedAlignerModels),
                    ["diarization"] = ToDtoDict(ModelRegistry.Default.DiarizationModels),
                    ["bertOnnx"] = ToDtoDict(ModelRegistry.Default.BertOnnxModels)
                }
            };

            File.WriteAllText(path, JsonSerializer.Serialize(seed, JsonOptions));
            logger?.LogInformation("Metadata config not found; seeded default at '{MetadataPath}'. Edit it to customize tools/models.", path);
        }
        catch (Exception ex)
        {
            // A failed seed write does not block startup; continue with built-in defaults.
            logger?.LogWarning(ex, "Could not seed metadata config at '{MetadataPath}'.", path);
        }
    }

    private static Dictionary<string, ModelMetaDto> ToDtoDict(IReadOnlyDictionary<string, ModelMeta> source) =>
        source.ToDictionary(kv => kv.Key, kv => ToDto(kv.Value), StringComparer.OrdinalIgnoreCase);

    private static ToolMetaDto ToDto(ToolMeta meta) => new()
    {
        ToolName = meta.ToolName,
        DownloadUrl = meta.DownloadUrl,
        ArchiveType = meta.ArchiveType,
        ExecutableRelativePath = meta.ExecutableRelativePath,
        Version = meta.Version,
        ModelBaseUrl = meta.ModelBaseUrl,
        FileHash = meta.FileHash,
        Variants = meta.Variants?.ToDictionary(
            kv => kv.Key, kv => ToDto(kv.Value), StringComparer.OrdinalIgnoreCase)
    };

    private static ToolVariantDto ToDto(ToolVariant variant) => new()
    {
        DownloadUrl = variant.DownloadUrl,
        ArchiveType = variant.ArchiveType,
        ExecutableRelativePath = variant.ExecutableRelativePath,
        Description = variant.Description,
        FileHash = variant.FileHash
    };

    private static ModelMetaDto ToDto(ModelMeta meta) => new()
    {
        FileName = meta.FileName,
        DownloadUrl = meta.DownloadUrl,
        FileHash = meta.FileHash,
        DownloadType = meta.DownloadType switch
        {
            ModelDownloadType.Directory => "directory",
            ModelDownloadType.OnnxModelDirectory => "onnx-directory",
            _ => "single-file"
        },
        Files = meta.Files,
        OnnxModelType = meta.OnnxModelType,
        Subdirectory = meta.Subdirectory
    };

    // ---------- JSON structures ----------

    /// <summary>Root structure of the metadata JSON.</summary>
    public sealed class MetadataFileDto
    {
        /// <summary>Tool entries, keyed by tool identifier.</summary>
        public Dictionary<string, ToolMetaDto>? Tools { get; set; }
        /// <summary>Model entries, keyed first by category (whisper, fasterWhisper, qwen3Asr, and so on), then by model name.</summary>
        public Dictionary<string, Dictionary<string, ModelMetaDto>>? Models { get; set; }
    }

    /// <summary>JSON structure for a tool entry.</summary>
    public sealed class ToolMetaDto
    {
        /// <summary>Tool identifier, such as whispercpp.</summary>
        public string? ToolName { get; set; }
        /// <summary>Default download package URL (zip or tar.gz).</summary>
        public string? DownloadUrl { get; set; }
        /// <summary>Archive type: "zip" or "tar.gz".</summary>
        public string? ArchiveType { get; set; }
        /// <summary>Path to the executable relative to the extracted package root.</summary>
        public string? ExecutableRelativePath { get; set; }
        /// <summary>Tool version.</summary>
        public string? Version { get; set; }
        /// <summary>Base URL for models or weights required at runtime; optional.</summary>
        public string? ModelBaseUrl { get; set; }
        /// <summary>Optional SHA-256 hash of the download package in lowercase hexadecimal; null disables verification.</summary>
        public string? FileHash { get; set; }
        /// <summary>Download variants keyed by device.</summary>
        public Dictionary<string, ToolVariantDto>? Variants { get; set; }
    }

    /// <summary>JSON structure for a device-specific tool variant.</summary>
    public sealed class ToolVariantDto
    {
        /// <summary>Download package URL for this variant; null falls back to the tool default.</summary>
        public string? DownloadUrl { get; set; }
        /// <summary>Archive type for this variant.</summary>
        public string? ArchiveType { get; set; }
        /// <summary>Path to the executable relative to the extracted variant package.</summary>
        public string? ExecutableRelativePath { get; set; }
        /// <summary>User-facing variant description, such as "CUDA 12.4 build".</summary>
        public string? Description { get; set; }
        /// <summary>Optional lowercase hexadecimal SHA-256 hash; null falls back to the tool default.</summary>
        public string? FileHash { get; set; }
    }

    /// <summary>JSON structure for a model entry.</summary>
    public sealed class ModelMetaDto
    {
        /// <summary>Local filename for single-file models.</summary>
        public string? FileName { get; set; }
        /// <summary>Model download URL.</summary>
        public string? DownloadUrl { get; set; }
        /// <summary>Optional lowercase hexadecimal SHA-256 hash; null disables verification.</summary>
        public string? FileHash { get; set; }
        /// <summary>Download type: "single-file", "directory", or "onnx-directory".</summary>
        public string? DownloadType { get; set; }
        /// <summary>Relative paths of files to download for directory and ONNX models.</summary>
        public List<string>? Files { get; set; }
        /// <summary>ONNX task type, such as token_classification or embedding.</summary>
        public string? OnnxModelType { get; set; }
        /// <summary>Optional subdirectory for the model within the download directory.</summary>
        public string? Subdirectory { get; set; }
    }
}
