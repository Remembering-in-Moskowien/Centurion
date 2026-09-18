using Centurion.Models.Metadata;
using Xunit;

namespace Centurion.Tests;

public sealed class MetadataJsonLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"centurion_meta_test_{Guid.NewGuid():N}");

    public MetadataJsonLoaderTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void LoadOrDefault_MissingFile_SeedsDefaultAndFallsBackToBuiltIn()
    {
        var path = Path.Combine(_tempDir, "metadata.json");

        var catalog = MetadataJsonLoader.LoadOrDefault(path);

        // 缺失时应当生成种子文件
        Assert.True(File.Exists(path), "Seed file should be written when missing.");
        // 并回退到内置默认注册表
        Assert.True(catalog.Tools.Tools.ContainsKey("whispercpp"));
        Assert.True(catalog.Tools.Tools.ContainsKey("crispasr"));
        Assert.True(catalog.Models.Qwen3AsrModels.ContainsKey("qwen3-asr-1.7b"));
        Assert.True(catalog.Models.Qwen3ForcedAlignerModels.ContainsKey("qwen3-forced-aligner-0.6b-f16"));
        Assert.True(catalog.Models.DiarizationModels.ContainsKey("voxceleb_resnet293_LM"));
    }

    [Fact]
    public void LoadOrDefault_ValidJson_LoadsCustomEntries()
    {
        var path = Path.Combine(_tempDir, "custom.json");
        File.WriteAllText(path, """
        {
          "tools": {
            "mytool": {
              "downloadUrl": "https://example.com/tool.zip",
              "archiveType": "zip",
              "executableRelativePath": "tool.exe",
              "version": "v1.0"
            }
          },
          "models": {
            "qwen3Asr": {
              "my-model": {
                "fileName": "my-model.gguf",
                "downloadUrl": "https://example.com/my-model.gguf",
                "downloadType": "single-file"
              }
            },
            "whisper": {
              "dir-model": {
                "downloadUrl": "https://example.com/dir/",
                "downloadType": "directory",
                "files": ["config.json", "model.bin"]
              }
            },
            "bertOnnx": {
              "onnx-model": {
                "downloadUrl": "https://example.com/onnx/",
                "downloadType": "onnx-directory",
                "files": ["model.onnx", "config.json"],
                "onnxModelType": "token_classification",
                "subdirectory": "onnx"
              }
            }
          }
        }
        """);

        var catalog = MetadataJsonLoader.LoadOrDefault(path);

        // 工具条目：ToolName 缺省时回退为字典键
        var tool = catalog.Tools.Tools["mytool"];
        Assert.Equal("mytool", tool.ToolName);
        Assert.Equal("https://example.com/tool.zip", tool.DownloadUrl);
        Assert.Equal("tool.exe", tool.ExecutableRelativePath);
        Assert.Equal("v1.0", tool.Version);

        // 单文件模型
        var single = catalog.Models.Qwen3AsrModels["my-model"];
        Assert.Equal(ModelDownloadType.SingleFile, single.DownloadType);
        Assert.Equal("my-model.gguf", single.FileName);
        Assert.Equal("https://example.com/my-model.gguf", single.DownloadUrl);

        // 目录模型
        var dir = catalog.Models.WhisperModels["dir-model"];
        Assert.Equal(ModelDownloadType.Directory, dir.DownloadType);
        Assert.Equal(["config.json", "model.bin"], dir.Files);

        // ONNX 目录模型
        var onnx = catalog.Models.BertOnnxModels["onnx-model"];
        Assert.Equal(ModelDownloadType.OnnxModelDirectory, onnx.DownloadType);
        Assert.Equal("token_classification", onnx.OnnxModelType);
        Assert.Equal("onnx", onnx.Subdirectory);
    }

    [Fact]
    public void LoadOrDefault_CorruptJson_FallsBackToBuiltIn()
    {
        var path = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(path, "{ not valid json !!");

        var catalog = MetadataJsonLoader.LoadOrDefault(path);

        Assert.True(catalog.Tools.Tools.ContainsKey("crispasr"));
        Assert.True(catalog.Models.DiarizationModels.ContainsKey("voxceleb_resnet293_LM"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* 忽略清理失败 */ }
    }
}
