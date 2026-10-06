using System.Text.Json;
using Centurion.Core.Infrastructure;
using Microsoft.ML.OnnxRuntime;

namespace Centurion.Core.Workflow.Strategy.Translation;

/// <summary>
/// OPUS-MT (Helsinki-NLP Marian) local inference engine built on ONNX Runtime.
/// Runs the encoder once per line, then beam-searches the decoder autoregressively
/// using the KV-cache decoder (decoder_with_past). Handles both the int8 quantized
/// build (onnx/*_quantized.onnx, the default) and the fp32 build (onnx/*.onnx),
/// auto-detected from the files present in the model folder.
/// </summary>
internal sealed class OpusMtEngine : IDisposable
{
    private const int EncoderLayers = 6;
    private const int DecoderDim = 512;

    private readonly string _modelFolder;
    private readonly int _decoderStartId;
    private readonly int _eosId;
    private readonly int _padId;
    private readonly int _vocabSize;
    private readonly Lock _initLock = new();
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private InferenceSession? _decoderWithPast;
    private bool _disposed;

    /// <summary>Creates the engine; sessions are loaded lazily on the first translation.</summary>
    /// <param name="modelFolder">OPUS-MT model directory (must contain onnx/*.onnx and config.json).</param>
    public OpusMtEngine(string modelFolder)
    {
        _modelFolder = modelFolder;

        // Load generation ids from config.json, falling back to the standard Marian values.
        var configPath = Path.Combine(modelFolder, "config.json");
        if (File.Exists(configPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            _decoderStartId = GetInt(root, "decoder_start_token_id", 65000);
            _eosId = GetInt(root, "eos_token_id", 0);
            _padId = GetInt(root, "pad_token_id", 65000);
            _vocabSize = GetInt(root, "decoder_vocab_size", 65001);
        }
        else
        {
            _decoderStartId = 65000;
            _eosId = 0;
            _padId = 65000;
            _vocabSize = 65001;
        }
    }

    private static int GetInt(JsonElement root, string property, int fallback)
        => root.TryGetProperty(property, out var el) && el.TryGetInt32(out var v) ? v : fallback;

    private static string[] PresentFetchNames() =>
        Enumerable.Range(0, EncoderLayers)
            .SelectMany(layer => new[]
            {
                $"present.{layer}.decoder.key",
                $"present.{layer}.decoder.value",
                $"present.{layer}.encoder.key",
                $"present.{layer}.encoder.value"
            })
            .ToArray();

    private static string[] PresentDecoderFetchNames() =>
        Enumerable.Range(0, EncoderLayers)
            .SelectMany(layer => new[]
            {
                $"present.{layer}.decoder.key",
                $"present.{layer}.decoder.value"
            })
            .ToArray();

    /// <summary>
    /// Translates a single line of text into the model's target language.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <param name="beamSize">Beam size; 1 = greedy decoding.</param>
    /// <param name="maxLength">Maximum decoded token count.</param>
    /// <returns>The translated text (may be empty when generation yields nothing).</returns>
    public string Translate(string text, int beamSize, int maxLength)
    {
        EnsureSessions();

        var tokenizer = new OpusMtTokenizer(_modelFolder);
        var inputIds = tokenizer.Encode(text).ToList();
        inputIds.Add(_eosId);

        // Encoder: input_ids + attention mask (all ones; no padding for a single line).
        var encoderHidden = RunEncoder(inputIds);

        var beams = new List<BeamState>
        {
            new() { Ids = [_decoderStartId], Score = 0 }
        };

        for (var step = 0; step < Math.Max(1, maxLength); step++)
        {
            var candidates = new List<BeamState>();
            foreach (var beam in beams.Where(b => !b.Finished))
            {
                var (logits, decoderPasts, encoderPasts) = RunDecoder(beam, encoderHidden, inputIds.Count);
                // Suppress the pad token (Marian's bad_words_ids) and compute log-softmax over the vocabulary.
                if (_padId >= 0 && _padId < logits.Length)
                    logits[_padId] = float.NegativeInfinity;
                var logProbs = LogSoftmax(logits);

                foreach (var (token, logProb) in TopK(logProbs, Math.Max(1, beamSize)))
                {
                    var nextIds = new List<int>(beam.Ids) { token };
                    candidates.Add(new BeamState
                    {
                        Ids = nextIds,
                        Score = beam.Score + logProb,
                        DecoderPasts = decoderPasts,
                        EncoderPasts = encoderPasts,
                        Finished = token == _eosId
                    });
                }
            }

            if (candidates.Count == 0)
                break;

            beams = candidates
                .OrderByDescending(c => c.Score)
                .Take(Math.Max(1, beamSize))
                .ToList();

            if (beams.All(b => b.Finished))
                break;
        }

        var best = beams.OrderByDescending(b => b.Score).First();
        var decodedIds = best.Ids.Skip(1).TakeWhile(id => id != _eosId).ToList();
        return tokenizer.Decode(decodedIds);
    }

    // ---------- ONNX runs ----------

    private float[] RunEncoder(IReadOnlyList<int> inputIds)
    {
        var ids = inputIds.Select(i => (long)i).ToArray(); // model expects int64 input_ids
        var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
        var shape = new long[] { 1, ids.Length };

        var feeds = new Dictionary<string, OrtValue>
        {
            ["input_ids"] = OrtValue.CreateTensorValueFromMemory(ids, shape),
            ["attention_mask"] = OrtValue.CreateTensorValueFromMemory(mask, shape)
        };
        try
        {
            using var results = _encoder!.Run(new RunOptions(), feeds, ["last_hidden_state"]);
            return results[0].GetTensorDataAsSpan<float>().ToArray();
        }
        finally
        {
            foreach (var value in feeds.Values)
                value.Dispose();
        }
    }

    private (float[] Logits, List<float[]> DecoderPasts, List<float[]> EncoderPasts) RunDecoder(
        BeamState beam, float[] encoderHidden, int sourceLength)
    {
        var hiddenShape = new long[] { 1, sourceLength, DecoderDim };
        var mask = Enumerable.Repeat(1L, sourceLength).ToArray();
        var maskShape = new long[] { 1, sourceLength };
        var inputIds = new long[] { beam.Ids[^1] };
        var inputShape = new long[] { 1, 1 };

        var firstStep = beam.EncoderPasts is null || beam.DecoderPasts is null;

        var feeds = new Dictionary<string, OrtValue>
        {
            ["encoder_attention_mask"] = OrtValue.CreateTensorValueFromMemory(mask, maskShape),
            ["input_ids"] = OrtValue.CreateTensorValueFromMemory(inputIds, inputShape)
        };

        // decoder_model takes encoder_hidden_states; decoder_with_past_model does not
        // (it consumes the encoder KV cache carried over as past_key_values).
        if (firstStep)
            feeds["encoder_hidden_states"] = OrtValue.CreateTensorValueFromMemory(encoderHidden, hiddenShape);

        if (!firstStep)
        {
            for (var i = 0; i < EncoderLayers; i++)
            {
                feeds[$"past_key_values.{i}.decoder.key"] = OrtValue.CreateTensorValueFromMemory(beam.DecoderPasts![i * 2], PastShape(beam.DecoderPasts![i * 2]));
                feeds[$"past_key_values.{i}.decoder.value"] = OrtValue.CreateTensorValueFromMemory(beam.DecoderPasts![i * 2 + 1], PastShape(beam.DecoderPasts![i * 2 + 1]));
                feeds[$"past_key_values.{i}.encoder.key"] = OrtValue.CreateTensorValueFromMemory(beam.EncoderPasts![i * 2], PastShape(beam.EncoderPasts![i * 2]));
                feeds[$"past_key_values.{i}.encoder.value"] = OrtValue.CreateTensorValueFromMemory(beam.EncoderPasts![i * 2 + 1], PastShape(beam.EncoderPasts![i * 2 + 1]));
            }
        }

        var fetchNames = firstStep
            ? new List<string> { "logits" }.Concat(PresentFetchNames()).ToList()
            : new List<string> { "logits" }.Concat(PresentDecoderFetchNames()).ToList();

        try
        {
            using var results = (firstStep ? _decoder! : _decoderWithPast!).Run(new RunOptions(), feeds, fetchNames);
            var logits = results[0].GetTensorDataAsSpan<float>().ToArray();

            var decoderPasts = new List<float[]>(EncoderLayers * 2);
            var encoderPasts = new List<float[]>(EncoderLayers * 2);
            if (firstStep)
            {
                for (var i = 0; i < EncoderLayers; i++)
                {
                    decoderPasts.Add(results[1 + i * 4].GetTensorDataAsSpan<float>().ToArray());
                    decoderPasts.Add(results[2 + i * 4].GetTensorDataAsSpan<float>().ToArray());
                    encoderPasts.Add(results[3 + i * 4].GetTensorDataAsSpan<float>().ToArray());
                    encoderPasts.Add(results[4 + i * 4].GetTensorDataAsSpan<float>().ToArray());
                }
            }
            else
            {
                for (var i = 0; i < EncoderLayers; i++)
                {
                    decoderPasts.Add(results[1 + i * 2].GetTensorDataAsSpan<float>().ToArray());
                    decoderPasts.Add(results[2 + i * 2].GetTensorDataAsSpan<float>().ToArray());
                }
                encoderPasts.AddRange(beam.EncoderPasts!);
            }

            return (logits, decoderPasts, encoderPasts);
        }
        finally
        {
            foreach (var value in feeds.Values)
                value.Dispose();
        }
    }

    private static long[] PastShape(float[] past)
    {
        // [1, 8, seq, 64]
        var seq = past.Length / (8 * 64);
        return [1, 8, seq, 64];
    }

    // ---------- Decoding helpers ----------

    private static float[] LogSoftmax(float[] logits)
    {
        var result = new float[logits.Length];
        var max = float.NegativeInfinity;
        foreach (var v in logits)
            max = Math.Max(max, v);
        var sum = 0.0;
        for (var i = 0; i < logits.Length; i++)
        {
            result[i] = MathF.Exp(logits[i] - max);
            sum += result[i];
        }
        var logSum = Math.Log(sum);
        for (var i = 0; i < result.Length; i++)
            result[i] = (float)((logits[i] - max) - logSum);
        return result;
    }

    private static IEnumerable<(int Token, float LogProb)> TopK(float[] logProbs, int k)
    {
        // Simple selection: scan the vocabulary once, keeping the k largest log-probs.
        var tokens = new int[k];
        var values = Enumerable.Repeat(float.NegativeInfinity, k).ToArray();

        for (var i = 0; i < logProbs.Length; i++)
        {
            var v = logProbs[i];
            if (v <= values[^1])
                continue;
            var pos = k - 1;
            while (pos > 0 && v > values[pos - 1])
                pos--;
            // shift right
            for (var j = k - 1; j > pos; j--)
            {
                values[j] = values[j - 1];
                tokens[j] = tokens[j - 1];
            }
            values[pos] = v;
            tokens[pos] = i;
        }

        for (var i = 0; i < k; i++)
            yield return (tokens[i], values[i]);
    }

    // ---------- Session lifecycle ----------

    private void EnsureSessions()
    {
        if (_encoder is not null)
            return;

        lock (_initLock)
        {
            if (_encoder is not null)
                return;

            // CPU-only session options: the 6-layer Marian encoder/decoder needs more DirectML
            // device memory than low-end GPUs (e.g. Radeon RX 560 2 GB) can provide; a DirectML
            // allocation failure at InferenceSession.Init is an un-catchable native access
            // violation, so translation deliberately stays on CPU while ASR/splitting use GPU.
            var options = OnnxSessionFactory.CreateSessionOptions(preferGpu: false);

            var encoderPath = ResolveModelFile("encoder_model");
            var decoderPath = ResolveModelFile("decoder_model");
            var decoderWithPastPath = ResolveModelFile("decoder_with_past_model");
            if (encoderPath is null || decoderPath is null || decoderWithPastPath is null)
                throw new FileNotFoundException(
                    "OPUS-MT ONNX models not found (run 'Centurion models install <pair>' first).", _modelFolder);

            _encoder = new InferenceSession(encoderPath, options);
            _decoder = new InferenceSession(decoderPath, options);
            _decoderWithPast = new InferenceSession(decoderWithPastPath, options);
        }
    }

    /// <summary>Resolves onnx/{name}_quantized.onnx first (int8 default), then onnx/{name}.onnx (fp32).</summary>
    private string? ResolveModelFile(string name)
    {
        var quantized = Path.Combine(_modelFolder, "onnx", name + "_quantized.onnx");
        if (File.Exists(quantized))
            return quantized;
        var fp32 = Path.Combine(_modelFolder, "onnx", name + ".onnx");
        return File.Exists(fp32) ? fp32 : null;
    }

    /// <summary>One beam hypothesis in the search tree.</summary>
    private sealed class BeamState
    {
        public required List<int> Ids { get; init; }
        public required double Score { get; init; }
        public List<float[]>? DecoderPasts { get; set; }
        public List<float[]>? EncoderPasts { get; set; }
        public bool Finished { get; init; }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _encoder?.Dispose();
        _decoder?.Dispose();
        _decoderWithPast?.Dispose();
    }
}
