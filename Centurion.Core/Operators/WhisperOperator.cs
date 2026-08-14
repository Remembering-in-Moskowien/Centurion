using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Centurion.Core.Exceptions;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;
using Centurion.Core.Models;
using Centurion.Core.Tools;

namespace Centurion.Core.Operators;

/// <summary>
/// 基于 whisper-cli 进程的转录算子。
/// 不继承任何基类，通过 ModelManager 管理模型文件。
/// </summary>
public class WhisperCliOperator : IOperators
{
    private readonly ModelManager _modelManager;
    private readonly IBinaryLocator _binaryLocator;
    private string _cliBinaryPath;
    private Process? _runningProcess;
    private bool _disposed;

    /// <summary>
    /// 构造函数，注入 ModelManager
    /// </summary>
    public WhisperCliOperator(ModelManager modelManager, IBinaryLocator binaryLocator)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _binaryLocator = binaryLocator;
        _cliBinaryPath = string.Empty;
    }

    /// <summary>
    /// 确保模型和二进制工具可用
    /// </summary>
    public async Task EnsureTargetAvailableAsync()
    {
        // 1. 确保模型文件存在并校验
        await _modelManager.EnsureModelAvailableAsync();

        // 2. 查找 whisper-cli 二进制
        if (!string.IsNullOrEmpty(_cliBinaryPath) && File.Exists(_cliBinaryPath))
            return;

        var binFileName = OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli";
        _cliBinaryPath = _binaryLocator.Locate(binFileName, "whisper-cli", "build/bin");
    }

    /// <summary>
    /// 执行转录请求
    /// </summary>
    public async Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();

        if (request.Payload is not WhisperTranscribePayload payload)
            throw new ArgumentException("Payload 必须为 WhisperTranscribePayload", nameof(request));

        var psi = new ProcessStartInfo(_cliBinaryPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        var args = psi.ArgumentList;
        args.Add("-m");
        args.Add(_modelManager.ModelFilePath);
        args.Add("-l");
        args.Add(payload.Language);
        args.Add("--output-json-full");
        if (!string.IsNullOrEmpty(payload.InitialPrompt))
        {
            args.Add("--prompt");
            args.Add(payload.InitialPrompt);
        }

        args.Add(payload.FilePath);

        using var proc = Process.Start(psi)!;
        _runningProcess = proc;
        string stdErrText;

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
            throw new WhisperProcessException("whisper-cli 执行失败，退出码", proc.ExitCode, stdErrText);

        var jsonTempPath = $"{payload.FilePath}.json";
        if (!File.Exists(jsonTempPath))
            throw new FileNotFoundException("whisper-cli 未输出 JSON 文件", jsonTempPath);

        var jsonRaw = await File.ReadAllTextAsync(jsonTempPath, cancellationToken);
        var jsonOpt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var transcript = JsonSerializer.Deserialize<WhisperTranscriptResult>(jsonRaw, jsonOpt)!;
        var tokens = transcript.Transcription.SelectMany(transcriptionItem =>
            transcriptionItem.Tokens ?? throw new InvalidOperationException("转录结果为空")).ToList();

        return (TResult)(object)tokens;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_runningProcess != null && !_runningProcess.HasExited)
        {
            _runningProcess.Kill();
            _runningProcess.WaitForExit(5000);
        }

        _runningProcess?.Dispose();
        _runningProcess = null;
        _disposed = true;
    }
}