// Centurion.Core/Models/Metadata/MetadataJsonLoader.cs

using System.Text.Json;

namespace Centurion.Core.Models.Metadata;

/// <summary>
/// 程序启动时的元数据加载结果：包含工具注册表与模型注册表实例。
/// </summary>
public sealed class MetadataCatalog
{
    public required ToolRegistry Tools { get; init; }
    public required ModelRegistry Models { get; init; }
}

/// <summary>
/// 元数据外部 JSON 配置加载器。
/// <para>
/// 加载顺序：
/// 1. 显式传入的路径（AddCenturionCore 参数）优先；
/// 2. 其次环境变量 <c>CENTURION_METADATA_PATH</c>；
/// 3. 再次默认候选路径（可执行目录 / 当前目录下的 config\metadata.json）。
/// </para>
/// <para>
/// 若目标 JSON 存在则加载之；不存在或解析失败时回退到内置默认注册表
/// （<see cref="ToolRegistry.Default"/> / <see cref="ModelRegistry.Default"/>），
/// 并尝试在默认路径写出种子 JSON 文件，便于用户按需编辑。
/// </para>
/// </summary>
public static class MetadataJsonLoader
{
    /// <summary>环境变量名：覆盖元数据 JSON 路径。</summary>
    public const string EnvVarName = "CENTURION_METADATA_PATH";

    private const string DefaultFileName = "metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 加载元数据。外部 JSON 缺失或损坏时回退内置默认，并尝试生成种子文件。
    /// </summary>
    public static MetadataCatalog LoadOrDefault(string? explicitPath = null)
    {
        var path = ResolvePath(explicitPath);

        if (File.Exists(path))
        {
            try
            {
                return LoadFromFile(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Centurion] Failed to load metadata config from '{path}': {ex.Message}");
                Console.Error.WriteLine("[Centurion] Falling back to built-in default metadata.");
            }
        }
        else
        {
            WriteSeedFile(path);
        }

        return new MetadataCatalog
        {
            Tools = ToolRegistry.Default,
            Models = ModelRegistry.Default
        };
    }

    /// <summary>
    /// 解析元数据 JSON 的最终路径。
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

    // ---------- 加载 ----------

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
                Version = dto.Version
            };
        }
        return new ToolRegistry(dict);
    }

    private static ModelRegistry BuildModels(Dictionary<string, Dictionary<string, ModelMetaDto>>? dtoModels)
    {
        return new ModelRegistry(
            BuildModelDict(dtoModels, "whisper"),
            BuildModelDict(dtoModels, "fasterWhisper"),
            BuildModelDict(dtoModels, "qwen3Asr"),
            BuildModelDict(dtoModels, "qwen3ForcedAligner"),
            BuildModelDict(dtoModels, "diarization"),
            BuildModelDict(dtoModels, "bertOnnx"));
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
                dto.Subdirectory),
            ModelDownloadType.OnnxModelDirectory => new ModelMeta(
                dto.DownloadUrl ?? throw new InvalidOperationException($"Model '{key}' is missing 'downloadUrl'."),
                dto.Files ?? throw new InvalidOperationException($"Model '{key}' (onnx-directory) requires a non-empty 'files' list."),
                dto.OnnxModelType ?? throw new InvalidOperationException($"Model '{key}' (onnx-directory) requires 'onnxModelType'."),
                dto.Subdirectory),
            _ => new ModelMeta(
                dto.FileName ?? throw new InvalidOperationException($"Model '{key}' is missing 'fileName'."),
                dto.DownloadUrl ?? throw new InvalidOperationException($"Model '{key}' is missing 'downloadUrl'."))
        };
    }

    private static ModelDownloadType ParseDownloadType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "single-file" or "singlefile" => ModelDownloadType.SingleFile,
        "directory" => ModelDownloadType.Directory,
        "onnx-directory" or "onnx" => ModelDownloadType.OnnxModelDirectory,
        _ => throw new InvalidOperationException($"Unknown model download type: '{value}'.")
    };

    // ---------- 种子文件 ----------

    private static void WriteSeedFile(string path)
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
            Console.Error.WriteLine($"[Centurion] Metadata config not found; seeded default at '{path}'. Edit it to customize tools/models.");
        }
        catch (Exception ex)
        {
            // 种子文件写出失败不影响启动：继续使用内置默认
            Console.Error.WriteLine($"[Centurion] Could not seed metadata config at '{path}': {ex.Message}");
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
        Version = meta.Version
    };

    private static ModelMetaDto ToDto(ModelMeta meta) => new()
    {
        FileName = meta.FileName,
        DownloadUrl = meta.DownloadUrl,
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

    // ---------- JSON 结构 ----------

    /// <summary>元数据 JSON 根结构。</summary>
    public sealed class MetadataFileDto
    {
        public Dictionary<string, ToolMetaDto>? Tools { get; set; }
        public Dictionary<string, Dictionary<string, ModelMetaDto>>? Models { get; set; }
    }

    /// <summary>工具条目 JSON 结构。</summary>
    public sealed class ToolMetaDto
    {
        public string? ToolName { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ArchiveType { get; set; }
        public string? ExecutableRelativePath { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>模型条目 JSON 结构。</summary>
    public sealed class ModelMetaDto
    {
        public string? FileName { get; set; }
        public string? DownloadUrl { get; set; }
        public string? DownloadType { get; set; }
        public List<string>? Files { get; set; }
        public string? OnnxModelType { get; set; }
        public string? Subdirectory { get; set; }
    }
}
