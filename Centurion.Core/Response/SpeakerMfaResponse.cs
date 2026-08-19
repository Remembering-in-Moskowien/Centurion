// SpeakerMfaResponse.cs
using Centurion.Core.Models;

namespace Centurion.Core.Response;

public class SpeakerMfaResponse
{
    public required List<Sentence> Sentences { get; init; }
}