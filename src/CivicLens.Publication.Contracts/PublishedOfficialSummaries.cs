namespace CivicLens.Publication.Contracts;

/// <summary>One summary row for each official of a release. The search service never reads it.</summary>
public sealed record PublishedOfficialSummaries
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public required PublishedOfficialSummary[] Officials { get; init; }

    public void Validate()
    {
        if (Officials is null || Officials.Length > PublishedOfficials.MaximumOfficials || Officials.Any(official => official is null))
            throw new InvalidDataException("Published official summaries are invalid.");
        foreach (var official in Officials) official.Validate();
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Officials.Select(official => official.Id)))
            throw new InvalidDataException("Published official summary IDs must be distinct.");
    }
}
