namespace CivicLens.PublicWeb.Publication;

/// <summary>Published definitions and source wording for the exact bill revision whose text the page shows.</summary>
public sealed record BillTermContext(string RecordId, int RevisionNumber, BillKeyTermData[] Terms, string? Label, string CongressGovUrl,
    string? SourceLabel = null);
