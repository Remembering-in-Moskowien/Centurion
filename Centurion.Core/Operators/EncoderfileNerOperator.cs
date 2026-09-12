using System.Text.Json;
using Centurion.Core.Abstractions;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Centurion.Core.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Operators;

public sealed record EncoderfileEntity(string Text, string Label, int? Start = null, int? End = null);

public sealed class EncoderfileNerOperator(
    EncoderfileManager encoderfileManager,
    IServiceProvider serviceProvider,
    ILogger<EncoderfileNerOperator> logger) : PipelineOperatorBase(logger)
{
    public override string Name => "Encoderfile NER";

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0) throw new InvalidOperationException("No current sentences are available for Encoderfile NER.");

        using var modelManager = new ModelManager("bert-base-ner", ModelRegistry.BertOnnxModels, serviceProvider, "bert");
        await modelManager.CheckHealthAsync(cancellationToken);
        var outputPath = Path.Combine(AppContext.BaseDirectory, "models", "encoderfile", "bert-base-ner.encoderfile");
        try
        {
            if (!File.Exists(outputPath))
                await encoderfileManager.BuildAsync(modelManager.ModelFolder, outputPath, modelManager.TargetMeta!.OnnxModelType!, cancellationToken);
        }
        catch (PlatformNotSupportedException ex)
        {
            logger.LogWarning(ex, "Encoderfile NER is unavailable on this platform; continuing without NER.");
            context.State.Warnings.Add(ex.Message);
            return;
        }

        var entities = new Dictionary<int, List<EncoderfileEntity>>();
        OnProgress(0, "Preparing Encoderfile NER...");
        for (var index = 0; index < sentences.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentence = sentences[index];
            if (string.IsNullOrWhiteSpace(sentence.Text)) continue;
            try
            {
                var output = await encoderfileManager.InferAsync(outputPath, sentence.Text, cancellationToken);
                entities[index] = ParseEntities(output);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var message = $"Encoderfile NER failed for sentence {index + 1}: {ex.Message}";
                logger.LogWarning(ex, "{Message}", message);
                context.State.Warnings.Add(message);
            }
            OnProgress((index + 1) * 100 / sentences.Count, $"NER sentence {index + 1}/{sentences.Count}");
        }
        context.State.Extensions["EncoderfileNerEntities"] = entities;
    }

    private static List<EncoderfileEntity> ParseEntities(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("entities", out var nested) ? nested : default;
            if (array.ValueKind != JsonValueKind.Array) return [];
            return array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new EncoderfileEntity(
                    ReadString(item, "word") ?? ReadString(item, "text") ?? string.Empty,
                    ReadString(item, "entity_group") ?? ReadString(item, "label") ?? ReadString(item, "entity") ?? "UNKNOWN",
                    ReadInt(item, "start"), ReadInt(item, "end")))
                .Where(entity => !string.IsNullOrWhiteSpace(entity.Text)).ToList();
        }
        catch (JsonException) { return []; }
    }

    private static string? ReadString(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? ReadInt(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
}
