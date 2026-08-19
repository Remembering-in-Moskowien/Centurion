using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Qourex.FasterWhisper.NET;

namespace Centurion.Core.Operators;

/// <summary>
/// 基于 FasterWhisper.NET 的纯 .NET 转录算子，使用本地模型目录。
/// </summary>
public class WhisperFasterOperator : IOperator<WhisperTranscribeRequest, WhisperTranscribeResponse>, IAsyncDisposable
{
    private readonly string _modelPath;
    private readonly string _device;
    private readonly string _computeType;
    private WhisperModel? _model;
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="modelPath">模型目录路径（包含 config.json, model.bin, vocabulary.txt）</param>
    /// <param name="device">推理设备："cpu" 或 "cuda"</param>
    /// <param name="computeType">计算精度："int8"（推荐）、"float16"、"float32"</param>
    public WhisperFasterOperator(string modelPath, string device = "cpu", string computeType = "int8")
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path cannot be empty.", nameof(modelPath));
        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Model directory not found: {modelPath}");

        _modelPath = modelPath;
        _device = device;
        _computeType = computeType;
    }

    public async Task EnsureTargetAvailableAsync()
    {
        if (_model == null)
        {
            // 直接加载本地目录（无需环境变量）
            _model = await Task.Run(() => new WhisperModel(_modelPath, _device, _computeType));
        }
    }

    public async Task<WhisperTranscribeResponse> ProcessAsync(
        OperatorsRequest<WhisperTranscribeRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();

        var payload = request.Payload;
        var audioPath = payload.FilePath;
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        var options = new WhisperOptions
        {
            WordTimestamps = true,
            MedianFilterWidth = 7,
            InitialPrompt = payload.InitialPrompt,
            BeamSize = 1,
            ReturnScores = true,
            ReturnNoSpeechProb = true,
            ConditionOnPreviousText = true
        };

        IEnumerable<WhisperSegment> segments;
        try
        {
            segments = _model!.Transcribe(
                audioPath,
                language: payload.Language ?? "en",
                options: options);
        }
        catch (Exception ex)
        {
            throw new WhisperProcessException("FasterWhisper transcription failed.", -1, ex.Message);
        }

        var wordTimings = segments
            .SelectMany(seg => seg.Words ?? Enumerable.Empty<WhisperWord>())
            .Where(w => !string.IsNullOrWhiteSpace(w.Word) && !IsSpecialToken(w.Word))
            .Select(w => new Word
            {
                Text = w.Word,
                Start = w.Start * 1000,
                End = w.End * 1000,
                Speaker = "UNKNOWN"
            })
            .ToList();

        if (wordTimings.Count == 0)
            throw new InvalidOperationException("No valid words extracted from transcription.");

        var response = new WhisperTranscribeResponse
        {
            Sentence = new Sentence
            {
                Start = wordTimings.First().Start,
                End = wordTimings.Last().End,
                Words = wordTimings
            }
        };

        return response;
    }

    private static bool IsSpecialToken(string text) =>
        text.StartsWith("[_") && text.EndsWith("]") &&
        (text.Contains("_BEG_") || text.Contains("_TT_") ||
         text.Contains("_EOT_") || text.Contains("_SOT_"));

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_model != null)
        {
            await Task.Run(() => _model.Dispose());
            _model = null;
        }
    }
}