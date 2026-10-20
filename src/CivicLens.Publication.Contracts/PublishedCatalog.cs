namespace CivicLens.Publication.Contracts;

/// <summary>The names of the issues and policy areas that the records of one release use.</summary>
public sealed record PublishedCatalog
{
    public const int MaximumEntries = 4000;

    public required PublishedCatalogEntry[] Issues { get; init; }
    public required PublishedCatalogEntry[] PolicyAreas { get; init; }

    public void Validate()
    {
        RequireEntries(Issues);
        RequireEntries(PolicyAreas);
    }

    private static void RequireEntries(PublishedCatalogEntry[]? entries)
    {
        if (entries is null || entries.Length > MaximumEntries || entries.Any(entry => entry is null || !PublishedEvidenceRules.IsBoundedText(entry.Id) || !PublishedEvidenceRules.IsBoundedText(entry.Name)) ||
            !PublishedEvidenceRules.AreDistinctAndNotBlank(entries.Select(entry => entry.Id)))
            throw new InvalidDataException("Published catalog is invalid.");
    }
}
