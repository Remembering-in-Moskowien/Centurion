using Centurion.Models.Workflow;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Centurion.Core.Utils.Audio;

/// <summary>
/// Silero VAD（ONNX，16 kHz 流式接口）语音活动检测器：
/// 逐 512 样本帧（32ms）推理语音概率（state 跨帧保持），概率 ≥ 0.5 判为语音帧，
/// 再按合并间隙/最短时长聚合成语音段。区分语音与纯器乐/静音，
/// 供「去除器乐段」的聚合预处理使用；能量型 <see cref="VadDetector"/> 为无模型时的降级路径。
/// </summary>
public sealed class SileroVadDetector : IDisposable
{
    private const int FrameSize = 512;      // 32ms @ 16kHz
    private const int StateSize = 128;      // LSTM state per layer
    private const double FrameSeconds = 0.032;

    private readonly InferenceSession _session;
    private readonly bool _inputHasBatchDim;
    private bool _disposed;

    /// <summary>最近一次检测的逐帧语音概率（诊断用；检测前为 null）。</summary>
    public float[]? SpeechProbabilities { get; private set; }

    /// <summary>
    /// 创建检测器。
    /// </summary>
    /// <param name="modelPath">silero_vad.onnx 路径。</param>
    /// <exception cref="InvalidOperationException">模型加载失败。</exception>
    public SileroVadDetector(string modelPath)
    {
        _session = new InferenceSession(modelPath, new SessionOptions
        {
            EnableMemoryPattern = false
        });
        // v4 接口 input[1,512]（rank 2）；v5 接口 input[1,1,512]（rank 3）——按模型实际形状适配
        _inputHasBatchDim = _session.InputMetadata["input"].Dimensions.Length == 3;
    }

    /// <summary>
    /// 检测语音段（要求 16 kHz 单声道样本）。
    /// </summary>
    /// <param name="samples">16 kHz 单声道样本（-1..1）。</param>
    /// <param name="sampleRate">采样率，必须为 16000。</param>
    /// <param name="options">段合并/最短时长参数（能量阈值项被忽略）。</param>
    /// <returns>语音段列表（毫秒，按源时间轴）。</returns>
    public List<VoiceSegment> Detect(float[] samples, int sampleRate, VadDetector.VadOptions options)
    {
        if (sampleRate != 16000)
            throw new InvalidOperationException($"Silero VAD requires 16 kHz audio, got {sampleRate} Hz.");

        var result = new List<VoiceSegment>();
        var frameCount = samples.Length / FrameSize;
        if (frameCount == 0)
            return result;

        // 1) 逐帧推理（state 跨帧保持）
        var input = new float[FrameSize];
        var state = new float[2 * StateSize];
        var sr = new long[] { 16000 };
        var probs = new float[frameCount];
        var speech = new bool[frameCount];

        for (var i = 0; i < frameCount; i++)
        {
            Array.Copy(samples, i * FrameSize, input, 0, FrameSize);
            var inputTensor = _inputHasBatchDim
                ? new DenseTensor<float>(input, new[] { 1, 1, FrameSize })
                : new DenseTensor<float>(input, new[] { 1, FrameSize });
            var prob = 0f;
            float[]? stateN = null;
            using (var results = _session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("state",
                    new DenseTensor<float>(state, new[] { 2, 1, StateSize })),
                NamedOnnxValue.CreateFromTensor("sr",
                    new DenseTensor<long>(sr, new[] { 1 }))
            }))
            {
                // v4/v5 输出顺序因模型而异（stateN/output 或 output/stateN）——按名称取，顺序无关
                foreach (var r in results)
                {
                    if (r.Name is "output" or "prob")
                        prob = r.AsTensor<float>()[0];
                    else if (r.Name == "stateN")
                        stateN = r.AsTensor<float>().ToArray();
                }
            }
            probs[i] = prob;
            speech[i] = prob >= 0.5f;
            if (stateN is not null)
                stateN.CopyTo(state, 0);
        }
        SpeechProbabilities = probs;

        // 2) 帧 → 段（合并间隙 / 最短时长，与能量 VAD 同一语义）
        var gapFrames = (int)Math.Max(0, Math.Round(options.MergeGapSeconds / FrameSeconds));
        var minFrames = (int)Math.Max(1, Math.Round(options.MinSpeechSeconds / FrameSeconds));

        var start = -1;
        var lastSpeech = -1;

        void Flush()
        {
            if (start < 0)
                return;
            var end = lastSpeech + 1;
            if (end - start >= minFrames)
            {
                result.Add(new VoiceSegment
                {
                    StartMs = start * FrameSeconds * 1000.0,
                    EndMs = end * FrameSeconds * 1000.0
                });
            }
            start = -1;
        }

        for (var i = 0; i < frameCount; i++)
        {
            if (speech[i])
            {
                if (start < 0)
                    start = i;
                lastSpeech = i;
            }
            else if (start >= 0 && i - lastSpeech > gapFrames)
            {
                Flush();
            }
        }
        Flush();

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _session.Dispose();
        _disposed = true;
    }
}
