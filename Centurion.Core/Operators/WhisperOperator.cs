using System.Diagnostics;
using System.Text.Json;
using System.Text;
using Centurion.Core.Exceptions;
using Centurion.Core.Operators.Base;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;

namespace Centurion.Core.Operators;

/// <summary>基于whisper-cli进程的转录算子，继承通用模型管理基类</summary>
public class WhisperCliOperator : ModelOperatorBase
{
    private string _cliBinaryPath;
    
    #region 重写模型字典（算子专属模型列表）
    protected override Dictionary<string, ModelMeta> ModelDict { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "tiny",
            new ModelMeta("ggml-tiny.bin", 
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
                "d4d8c35e89d3a377c36c8a8ba642a0073d32d6e31724932209a1239d63f58d3d")
        },
        {
            "base",
            new ModelMeta("ggml-base.en.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin",
                "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002")
        },
        {
            "small",
            new ModelMeta("ggml-small.bin", 
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
                "d7f3772fd9b44f3e8b473f8826913c39e8036c1804e4f3c42c44f2b7a09c2230")
        },
        {
            "medium",
            new ModelMeta("ggml-medium.bin", 
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
                "c9b43b6e84a37064d40e0b8e4f44e41b7f4d938d72d33a58d9a8a14c33232503")
        },
        {
            "large",
            new ModelMeta("ggml-large-v3.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
                "8c4f485a938a4f2e80d37e5a1b23d4d125f4f54c86b45ad2292f0444b1349f26")
        }
    };
    #endregion

    public WhisperCliOperator(string modelName = "base") : base(modelName)
    {
        _cliBinaryPath = string.Empty;
    }

    #region 重写模型分类文件夹
    protected override string GetModelCategoryFolder() => "whisper";
    #endregion

    #region 实现前置校验（二进制查找+模型下载+哈希校验）
    public override async Task EnsureTargetAvailableAsync()
    {
        // 1. 查找 whisper-cli 二进制
        if (!string.IsNullOrEmpty(_cliBinaryPath) && File.Exists(_cliBinaryPath))
            return;

        var binFileName = OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli";
        _cliBinaryPath = LocateBinary(binFileName, "whisper-cli", "build/bin");

        // 2. 模型不存在则下载（调用基类通用下载方法）
        if (!File.Exists(_modelFilePath))
        {
            await DownloadModelAsync();
        }

        // 3. 通用哈希校验（基类封装）
        VerifyModelHash();
    }
    #endregion

    #region 实现转录请求业务逻辑（仅whisper专属进程/JSON逻辑）
    public override async Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request, 
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();

        if (request.Payload is not WhisperTranscribePayload payload)
            throw new ArgumentException("Payload必须为WhisperTranscribePayload", nameof(request));
        
        var psi = new ProcessStartInfo(_cliBinaryPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        // whisper-cli 独有启动参数
        var args = psi.ArgumentList;
        args.Add("-m"); args.Add(_modelFilePath);
        args.Add("-l"); args.Add(payload.Language);
        args.Add("--output-json-full"); // JSON输出
        if (!string.IsNullOrEmpty(payload.InitialPrompt))
        {
            args.Add("--prompt");
            args.Add(payload.InitialPrompt);
        }
        args.Add(payload.FilePath);

        using var proc = Process.Start(psi)!;
        _runningProcess = proc;
        string stdErrText;
        ConsoleServices.Output.WriteLine("转录开始");

        try
        {
            cancellationToken.Register(() =>
            {
                if (!proc.HasExited) proc.Kill();
            });
            await proc.WaitForExitAsync(cancellationToken);
            stdErrText = await proc.StandardError.ReadToEndAsync(cancellationToken);
        }
        finally
        {
            _runningProcess = null;
        }

        if (proc.ExitCode != 0)
            throw new WhisperProcessException("whisper-cli执行失败，退出码", proc.ExitCode, stdErrText);

        var jsonTempPath = $"{payload.FilePath}.json";
        if (!File.Exists(jsonTempPath))
            throw new FileNotFoundException("whisper-cli未输出JSON文件", jsonTempPath);

        var jsonRaw = await File.ReadAllTextAsync(jsonTempPath, cancellationToken);
        var jsonOpt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var transcript = JsonSerializer.Deserialize<WhisperTranscriptResult>(jsonRaw, jsonOpt)!;
        var tokens = transcript.Transcription.SelectMany(transcriptionItem => transcriptionItem.Tokens ?? throw new InvalidOperationException("转录结果为空")).ToList();

        ConsoleServices.Output.WriteLine("转录成功");

        return (TResult)(object)tokens;
    }
    #endregion
}
