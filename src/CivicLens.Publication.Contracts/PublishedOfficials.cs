namespace CivicLens.Publication.Contracts;

/// <summary>The officials who have a published record in one release, each with the dated terms of the roster.</summary>
public sealed record PublishedOfficials
{
    public const int MaximumOfficials = 2000;
    public const int MaximumTerms = 64;
    public const int MaximumBytes = 32 * 1024 * 1024;

    public required PublishedOfficialProfile[] Officials { get; init; }

    public void Validate()
    {
        if (Officials is null || Officials.Length > MaximumOfficials || Officials.Any(official => official is null))
            throw new InvalidDataException("Published officials are invalid.");
        foreach (var official in Officials) official.Validate();
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Officials.Select(official => official.Id)))
            throw new InvalidDataException("Published official IDs must be distinct.");
    }
}
