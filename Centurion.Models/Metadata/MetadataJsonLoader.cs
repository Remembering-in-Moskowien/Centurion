using System.Text.Json;

namespace Centurion.Models.Metadata;

/// <summary>
/// 程序启动时的元数据加载结果：包含工具注册表与模型注册表实例。
/// </summary>
public sealed class MetadataCatalog
{
    /// <summary>已加载的工具注册表，按工具名索引。</summary>
    public required ToolRegistry Tools { get; init; }
    /// <summary>已加载的模型注册表，按模型类别索引。</summary>
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
                var catalog = LoadFromFile(path);
                var merged = MergeMissingDefaults(catalog);
                if (merged.Changed)
                {
                    // 程序升级引入的新工具/模型条目补入本地配置，保留用户自定义部分
                    System.Console.Error.WriteLine("[Centurion] New default entries merged into metadata config.");
                    WriteCatalogFile(path, merged.Catalog);
                }
                return merged.Catalog;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[Centurion] Failed to load metadata config from '{path}': {ex.Message}");
                System.Console.Error.WriteLine("[Centurion] Falling back to built-in default metadata.");
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
            BuildModelDict(dtoModels, "qwen3Tts"));
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

    // ---------- 缺失条目合并 ----------

    /// <summary>
    /// 把内置默认注册表中有、而本地配置缺失的工具/模型条目补入，
    /// 使用户自定义条目保留的同时，随程序升级自动获得新增能力。
    /// </summary>
    /// <param name="catalog">已加载的本地注册表。</param>
    /// <returns>合并结果（是否发生补充 + 合并后的注册表）。</returns>
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
        return new ModelRegistry(whisper, faster, qwen, aligner, diar, bert, tts);
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

    /// <summary>把注册表对象序列化写回配置文件。</summary>
    /// <param name="path">目标 JSON 路径。</param>
    /// <param name="catalog">待写入的注册表。</param>
    private static void WriteCatalogFile(string path, MetadataCatalog catalog)
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
                    ["bertOnnx"] = ToDtoDict(catalog.Models.BertOnnxModels)
                }
            };

            File.WriteAllText(path, JsonSerializer.Serialize(seed, JsonOptions));
        }
        catch (Exception ex)
        {
            // 写回失败不影响本次运行（内存中已合并）
            System.Console.Error.WriteLine($"[Centurion] Could not persist merged metadata at '{path}': {ex.Message}");
        }
    }

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
            System.Console.Error.WriteLine($"[Centurion] Metadata config not found; seeded default at '{path}'. Edit it to customize tools/models.");
        }
        catch (Exception ex)
        {
            // 种子文件写出失败不影响启动：继续使用内置默认
            System.Console.Error.WriteLine($"[Centurion] Could not seed metadata config at '{path}': {ex.Message}");
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

    // ---------- JSON 结构 ----------

    /// <summary>元数据 JSON 根结构。</summary>
    public sealed class MetadataFileDto
    {
        /// <summary>工具条目字典，键为工具标识名。</summary>
        public Dictionary<string, ToolMetaDto>? Tools { get; set; }
        /// <summary>模型条目字典，外层键为类别（whisper/fasterWhisper/qwen3Asr 等），内层键为模型名。</summary>
        public Dictionary<string, Dictionary<string, ModelMetaDto>>? Models { get; set; }
    }

    /// <summary>工具条目 JSON 结构。</summary>
    public sealed class ToolMetaDto
    {
        /// <summary>工具标识名（如 whispercpp）。</summary>
        public string? ToolName { get; set; }
        /// <summary>默认下载包 URL（zip 或 tar.gz）。</summary>
        public string? DownloadUrl { get; set; }
        /// <summary>压缩包类型（"zip" 或 "tar.gz"）。</summary>
        public string? ArchiveType { get; set; }
        /// <summary>解压后可执行文件相对包根目录的路径。</summary>
        public string? ExecutableRelativePath { get; set; }
        /// <summary>工具版本号。</summary>
        public string? Version { get; set; }
        /// <summary>工具运行所需模型/权重的基础下载地址，可为空。</summary>
        public string? ModelBaseUrl { get; set; }
        /// <summary>可选：下载包 SHA256 校验值（十六进制小写），为空时不校验。</summary>
        public string? FileHash { get; set; }
        /// <summary>按设备键的下载变体字典。</summary>
        public Dictionary<string, ToolVariantDto>? Variants { get; set; }
    }

    /// <summary>工具按设备变体的 JSON 结构。</summary>
    public sealed class ToolVariantDto
    {
        /// <summary>该变体的下载包 URL，为空则回退到工具基础值。</summary>
        public string? DownloadUrl { get; set; }
        /// <summary>该变体的压缩包类型。</summary>
        public string? ArchiveType { get; set; }
        /// <summary>该变体解压后可执行文件相对路径。</summary>
        public string? ExecutableRelativePath { get; set; }
        /// <summary>面向用户的变体说明（如 "CUDA 12.4 build"）。</summary>
        public string? Description { get; set; }
        /// <summary>可选：该变体下载包 SHA256 校验值（十六进制小写），为空回退到工具基础值。</summary>
        public string? FileHash { get; set; }
    }

    /// <summary>模型条目 JSON 结构。</summary>
    public sealed class ModelMetaDto
    {
        /// <summary>单文件模型的本地文件名（单文件类型时使用）。</summary>
        public string? FileName { get; set; }
        /// <summary>模型下载 URL。</summary>
        public string? DownloadUrl { get; set; }
        /// <summary>可选：下载文件 SHA256 校验值（十六进制小写），为空时不校验。</summary>
        public string? FileHash { get; set; }
        /// <summary>下载类型字符串（"single-file" / "directory" / "onnx-directory"）。</summary>
        public string? DownloadType { get; set; }
        /// <summary>目录/ONNX 模型需下载的文件相对路径列表。</summary>
        public List<string>? Files { get; set; }
        /// <summary>ONNX 模型任务类型（如 token_classification、embedding）。</summary>
        public string? OnnxModelType { get; set; }
        /// <summary>模型在下载目录中的子目录，可为空。</summary>
        public string? Subdirectory { get; set; }
    }
}
