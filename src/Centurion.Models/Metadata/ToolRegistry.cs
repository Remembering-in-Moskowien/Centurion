namespace Centurion.Models.Metadata;

/// <summary>
/// Device-specific tool variant, such as a CUDA, Vulkan, or CPU build.
/// Nullable fields override the <see cref="ToolMeta"/> base values when provided.
/// </summary>
public class ToolVariant
{
    /// <summary>Download package URL for this variant; null falls back to the tool default.</summary>
    public string? DownloadUrl { get; set; }
    /// <summary>Archive type for this variant: "zip" or "tar.gz".</summary>
    public string? ArchiveType { get; set; }
    /// <summary>Path to the executable relative to the extracted package root.</summary>
    public string? ExecutableRelativePath { get; set; }
    /// <summary>User-facing variant description, such as the required GPU model.</summary>
    public string? Description { get; set; }
    /// <summary>Optional SHA-256 hash for the variant package; null falls back to the tool default.</summary>
    public string? FileHash { get; set; }
}

/// <summary>Download and installation metadata for an external tool.</summary>
public class ToolMeta
{
    /// <summary>Tool identifier, such as "whispercpp" or "fasterwhisperxxl".</summary>
    public required string ToolName { get; set; }
    /// <summary>Default/fallback download package URL (zip or tar.gz).</summary>
    public required string DownloadUrl { get; set; }
    /// <summary>Archive type: "zip" or "tar.gz".</summary>
    public required string ArchiveType { get; set; }
    /// <summary>Path to the executable relative to the extracted package root.</summary>
    public required string ExecutableRelativePath { get; set; }
    /// <summary>Optional tool version.</summary>
    public string? Version { get; set; }
    /// <summary>Optional user-facing tool description (shown in provider listings).</summary>
    public string? Description { get; set; }
    /// <summary>
    /// Base download URL for models or weights required at runtime, such as a demucs-rs safetensors repository.
    /// Optional; null uses the tool's built-in default URL.
    /// </summary>
    public string? ModelBaseUrl { get; set; }
    /// <summary>
    /// Download variants keyed by device (cuda, vulkan, directml, cpu, or default).
    /// Device-specific matches take precedence over "default", which takes precedence over base fields.
    /// </summary>
    public IReadOnlyDictionary<string, ToolVariant>? Variants { get; set; }
    /// <summary>
    /// Optional SHA-256 hash for the download package, in lowercase hexadecimal.
    /// Null disables verification; when provided, the downloaded file is verified and deleted on mismatch.
    /// </summary>
    public string? FileHash { get; set; }
}

/// <summary>
/// Registry of tool metadata.
/// Loaded from external JSON by <see cref="MetadataJsonLoader"/> at startup;
/// falls back to <see cref="Default"/> when no external configuration is provided.
/// </summary>
public sealed class ToolRegistry
{
    /// <summary>
    /// Built-in default registry, used when external JSON is missing and as the seed file source.
    /// </summary>
    public static ToolRegistry Default { get; } = new(BuildDefaultTools());

    /// <summary>Registered tools, keyed by tool identifier.</summary>
    public IReadOnlyDictionary<string, ToolMeta> Tools { get; }

    /// <summary>Creates a registry from a tool dictionary.</summary>
    /// <param name="tools">Tool metadata keyed by tool identifier.</param>
    public ToolRegistry(IReadOnlyDictionary<string, ToolMeta> tools)
    {
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    private static IReadOnlyDictionary<string, ToolMeta> BuildDefaultTools() =>
        new Dictionary<string, ToolMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["whispercpp"] = new()
            {
                ToolName = "whispercpp",
                DownloadUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-bin-x64.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "whisper-cli.exe",
                Version = "v1.9.2",
                Variants = new Dictionary<string, ToolVariant>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cuda"] = new()
                    {
                        DownloadUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.2/whisper-cublas-12.4.0-bin-x64.zip",
                        ExecutableRelativePath = "whisper-cli.exe",
                        Description = "CUDA 12.4 build (requires NVIDIA GPU)"
                    }
                }
            },
            ["crispasr"] = new()
            {
                ToolName = "crispasr",
                DownloadUrl = "https://github.com/CrispStrobe/CrispASR/releases/download/v0.8.30/crispasr-windows-x86_64-cpu.zip",
                ArchiveType = "zip",
                ExecutableRelativePath = "crispasr.exe", // "crispasr" on Linux/macOS.
                Version = "v0.8.30"
            },
            ["polyvoice"] = new()
            {
                ToolName = "polyvoice",
                DownloadUrl = "https://github.com/ekhodzitsky/polyvoice/releases/download/v1.0.0/polyvoice-windows-x86_64.exe",
                ArchiveType = "direct", // single prebuilt .exe; no archive to extract
                ExecutableRelativePath = "polyvoice.exe",
                Version = "v1.0.0",
                Description = "Rust CPU speaker diarization (powerset segmentation + WeSpeaker ResNet34 + VBx/AHC)"
            },
            ["sherpa-onnx"] = new()
            {
                ToolName = "sherpa-onnx",
                DownloadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/sherpa-onnx-v1.13.8-win-x64-shared-MD-Release-no-tts.tar.bz2",
                ArchiveType = "tar.bz2",
                ExecutableRelativePath = "sherpa-onnx-offline-speaker-diarization.exe",
                Version = "v1.13.8",
                Description = "sherpa-onnx offline speaker diarization (pyannote segmentation + WeSpeaker embeddings)"
            }
        };
}
