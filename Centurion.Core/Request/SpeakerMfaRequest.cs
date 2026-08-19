// SpeakerMfaRequest.cs
using Centurion.Core.Models;

namespace Centurion.Core.Request;

public class SpeakerMfaRequest
{
    public required string AudioFilePath { get; init; }
    public required List<Sentence> Sentences { get; init; }
}