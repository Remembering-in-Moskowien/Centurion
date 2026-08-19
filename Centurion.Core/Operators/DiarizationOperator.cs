using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Managers;
using Centurion.Core.Metadata;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using SherpaOnnx;

namespace Centurion.Core.Operators;

/// <summary>
/// 基于 sherpa-onnx 的说话人分割算子（Diarization）。
/// 使用 ModelManager 自动管理模型文件（下载/校验）。
/// 接收音频文件路径和句子列表（包含起始/结束时间戳），
/// 对每个句子提取声纹嵌入，通过聚类分配说话人标签，
/// 并将标签写入每个句子的所有单词的 Speaker 属性。
/// </summary>
public class DiarizationOperator(IOptions<DiarizationOptions> options, IServiceProvider serviceProvider)
    : IOperator<DiarizationRequest, DiarizationResponse>, IAsyncDisposable
{
    private readonly DiarizationOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private SpeakerEmbeddingExtractor? _extractor;
    private bool _disposed;
    private const int SampleRate = 16000;  // 固定 16kHz
    private const int MinSegmentDurationMs = 200;

    /// <summary>
    /// 校验模型是否存在并初始化提取器（自动下载模型）
    /// </summary>
    public async Task EnsureTargetAvailableAsync()
    {
        if (_extractor != null)
            return;

        // 使用 ModelManager 确保模型存在
        using var modelManager = ActivatorUtilities.CreateInstance<ModelManager>(
            _serviceProvider,
            _options.ModelName,
            ModelRegistry.DiarizationModels,
            "diarization");

        await modelManager.EnsureModelAvailableAsync();
        var modelPath = modelManager.ModelFilePath;

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"模型文件不存在: {modelPath}");

        await Task.Run(() =>
        {
            try
            {
                var config = new SpeakerEmbeddingExtractorConfig
                {
                    Model = modelPath,
                    NumThreads = 1,
                    Debug = 0,
                    Provider = "cpu"
                };
                _extractor = new SpeakerEmbeddingExtractor(config);
            }
            catch (Exception ex)
            {
                throw new DiarizationException($"初始化 sherpa-onnx 提取器失败: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// 发送分割请求
    /// </summary>
    public async Task<DiarizationResponse> ProcessAsync(
        OperatorsRequest<DiarizationRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        var payload = request.Payload;
        return await DiarizeAsync(payload, cancellationToken);
    }

    /// <summary>
    /// 核心分割逻辑
    /// </summary>
    private async Task<DiarizationResponse> DiarizeAsync(
        DiarizationRequest payload,
        CancellationToken cancellationToken)
    {
        if (_extractor == null)
            throw new InvalidOperationException("提取器未初始化");

        var sentences = payload.Sentences;
        if (sentences == null || sentences.Count == 0)
            return new DiarizationResponse { Sentences = [] };

        // 1. 读取整个音频文件的 PCM 数据（float 数组）
        var audioSamples = await LoadAudioSamplesAsync(payload.AudioFilePath, cancellationToken);
        if (audioSamples == null || audioSamples.Length == 0)
            throw new DiarizationException("音频文件无有效数据");

        // 2. 为每个句子提取声纹嵌入
        var validSentences = new List<Sentence>();
        var embeddings = new List<float[]>();

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startMs = sentence.Start;
            var endMs = sentence.End;
            var durationMs = endMs - startMs;

            if (durationMs < MinSegmentDurationMs)
                continue;

            // 截取波形片段
            var segment = ExtractSegment(audioSamples, startMs, endMs);
            if (segment == null || segment.Length == 0)
                continue;

            // 使用 OnlineStream 提取嵌入
            float[] embedding;
            try
            {
                using var stream = _extractor.CreateStream();
                stream.AcceptWaveform(SampleRate, segment);
                if (!_extractor.IsReady(stream))
                {
                    continue;
                }
                embedding = _extractor.Compute(stream);
            }
            catch (Exception ex)
            {
                ConsoleServices.Output?.WriteWarning($"提取句子 ({startMs}-{endMs}) 嵌入失败: {ex.Message}");
                continue;
            }

            validSentences.Add(sentence);
            embeddings.Add(embedding);
        }

        if (validSentences.Count == 0)
            return new DiarizationResponse { Sentences = [] };

        // 3. 聚类
        var labels = ClusterEmbeddings(embeddings, _options.ClusterThreshold ?? 0.55);

        // 4. 将说话人标签写入每个句子的所有单词
        for (int i = 0; i < validSentences.Count; i++)
        {
            var speakerId = labels[i];
            var speakerLabel = $"Speaker{speakerId}";
            foreach (var word in validSentences[i].Words)
            {
                word.Speaker = speakerLabel;
            }
        }

        return new DiarizationResponse { Sentences = validSentences };
    }

    /// <summary>
    /// 异步加载音频文件的所有采样数据（float 归一化 [-1,1]）
    /// </summary>
    private async Task<float[]> LoadAudioSamplesAsync(string filePath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var reader = new WaveFileReader(filePath);

            if (reader.WaveFormat.SampleRate != SampleRate)
                throw new DiarizationException($"音频采样率必须为 {SampleRate}Hz，实际为 {reader.WaveFormat.SampleRate}Hz");
            if (reader.WaveFormat.Channels != 1)
                throw new DiarizationException("音频必须为单声道");

            var byteCount = (int)reader.Length;
            var bytes = new byte[byteCount];
            var bytesRead = 0;
            while (bytesRead < byteCount)
            {
                ct.ThrowIfCancellationRequested();
                var toRead = Math.Min(4096, byteCount - bytesRead);
                var read = reader.Read(bytes, bytesRead, toRead);
                if (read == 0) break;
                bytesRead += read;
            }
            if (bytesRead < byteCount)
                Array.Resize(ref bytes, bytesRead);

            var sampleCount = bytesRead / 2;
            var floats = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(bytes, i * 2);
                floats[i] = sample / 32768.0f;
            }
            return floats;
        }, ct);
    }

    /// <summary>
    /// 从全量音频样本中截取指定时间区间（毫秒）
    /// </summary>
    private float[]? ExtractSegment(float[] fullSamples, double startMs, double endMs)
    {
        var startSample = (int)(startMs * SampleRate / 1000.0);
        var endSample = (int)(endMs * SampleRate / 1000.0);
        if (startSample < 0) startSample = 0;
        if (endSample > fullSamples.Length) endSample = fullSamples.Length;
        if (startSample >= endSample) return null;

        var length = endSample - startSample;
        var segment = new float[length];
        Array.Copy(fullSamples, startSample, segment, 0, length);
        return segment;
    }

    /// <summary>
    /// 在线顺序聚类（基于余弦相似度）
    /// </summary>
    private List<int> ClusterEmbeddings(List<float[]> embeddings, double threshold)
    {
        if (embeddings.Count == 0) return [];

        var labels = new List<int> { 0 };
        var clusterCenters = new List<float[]> { embeddings[0] };

        for (int i = 1; i < embeddings.Count; i++)
        {
            var emb = embeddings[i];
            bool assigned = false;
            for (int c = 0; c < clusterCenters.Count; c++)
            {
                var sim = CosineSimilarity(emb, clusterCenters[c]);
                if (sim >= threshold)
                {
                    labels.Add(c);
                    // 更新簇中心（增量平均）
                    var center = clusterCenters[c];
                    var count = labels.Count(l => l == c);
                    for (int j = 0; j < center.Length; j++)
                        center[j] = (center[j] * (count - 1) + emb[j]) / count;
                    assigned = true;
                    break;
                }
            }
            if (!assigned)
            {
                clusterCenters.Add(emb);
                labels.Add(clusterCenters.Count - 1);
            }
        }
        return labels;
    }

    private double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("向量长度不匹配");
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    // ---------- 资源释放 ----------
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_extractor != null)
        {
            _extractor.Dispose();
            _extractor = null;
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// 说话人分割配置
/// </summary>
public class DiarizationOptions
{
    public string ModelName { get; set; } = "voxceleb_resnet293_LM";
    public double? ClusterThreshold { get; set; } = 0.55;
}