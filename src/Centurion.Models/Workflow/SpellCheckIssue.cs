namespace Centurion.Models.Workflow;

/// <summary>
/// A suspect found by one spell check run: the suspicious word, the sentence containing it, and candidate suggestions.
/// </summary>
public sealed class SpellCheckIssue
{
    /// <summary>Zero-based index of the suspicious word within its sentence.</summary>
    public int SentenceIndex { get; init; }

    /// <summary>The suspicious word as it appears.</summary>
    public string Word { get; init; } = "";

    /// <summary>Text of the sentence containing the suspicious word.</summary>
    public string Context { get; init; } = "";

    /// <summary>Candidate suggestions from Hunspell, at most 5.</summary>
    public List<string> Suggestions { get; init; } = [];
}
