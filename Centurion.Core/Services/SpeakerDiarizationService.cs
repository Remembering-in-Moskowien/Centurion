using Centurion.Core.Operators.Results;
using Centurion.Core.Services.Base;
using Centurion.Core.Services.Dto;
using Centurion.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Centurion.Core.Services;

public class SpeakerDiarizationService(
    [FromKeyedServices("vad")] ModelManager vadModelManager,
    [FromKeyedServices("wespeaker")] ModelManager wespeakerModelManager)
    : PythonServiceBase(modelManager: null)
{
    private readonly ModelManager _vadModelManager = vadModelManager ?? throw new ArgumentNullException(nameof(vadModelManager));
    private readonly ModelManager _wespeakerModelManager = wespeakerModelManager ?? throw new ArgumentNullException(nameof(wespeakerModelManager));

    protected override string ScriptName => "diarize_alternative.py";
    protected override int StartupDelayMs => 3000;

    public override async Task EnsureModelAvailableAsync()
    {
        await _vadModelManager.EnsureModelAvailableAsync();
        await _wespeakerModelManager.EnsureModelAvailableAsync();
    }

    public async Task<List<SpeakerSegment>> DiarizeAsync(
        string audioFile,
        int numSpeakers = 0,
        CancellationToken ct = default)
    {
        var payload = new
        {
            audio_file = audioFile,
            num_speakers = numSpeakers
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = await SendRequestAsync(json, ct);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<SpeakerDiarizationResult>(response, options);
        return result?.Segments ?? [];
    }
}