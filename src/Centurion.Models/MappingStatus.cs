namespace Centurion.Models;

/// <summary>Alignment result between a script word and a recognized audio word.</summary>
public enum MappingStatus
{
    /// <summary>The script word was successfully aligned with an audio word.</summary>
    Matched,
    /// <summary>The word appears only in the script; rendering usually inserts a placeholder gap.</summary>
    ScriptMissing,
    /// <summary>The word appears only in the audio transcription and has no script match, usually due to ad-lib speech.</summary>
    AudioExtra
}
