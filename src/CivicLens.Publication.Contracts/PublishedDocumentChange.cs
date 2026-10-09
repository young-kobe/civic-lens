namespace CivicLens.Publication.Contracts;

public sealed record PublishedDocumentChange
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public string? Significance { get; init; }
    public string? Limits { get; init; }
    public required string Institution { get; init; }
    public DateOnly? ChangeDate { get; init; }
    public PublishedCitation? ChangeDateEvidence { get; init; }
    public required PublishedOfficial[] Officials { get; init; }
    public required string[] IssueIds { get; init; }
    public required PublishedDocumentVersion Before { get; init; }
    public required PublishedDocumentVersion After { get; init; }
    public required PublishedChange[] Changes { get; init; }
    public required PublishedCitation[] Citations { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(RecordId) || RevisionNumber < 1 || string.IsNullOrWhiteSpace(Headline) ||
            string.IsNullOrWhiteSpace(Summary) || string.IsNullOrWhiteSpace(Institution))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        ValidateVersion(Before);
        ValidateVersion(After);
        if (Before.ExtractionId == After.ExtractionId)
            throw new InvalidDataException("Published record versions must differ.");
        if (Citations is null || Citations.Length is 0 or > 64)
            throw new InvalidDataException("Published record requires 1 to 64 citations.");
        foreach (var citation in Citations) ValidateCitation(citation);
        if ((ChangeDate is null) != (ChangeDateEvidence is null))
            throw new InvalidDataException("An actual change date requires exact evidence.");
        if (ChangeDateEvidence is not null) ValidateCitation(ChangeDateEvidence);
        ValidateLabels();
        if (Changes is null || Changes.Length == 0)
            throw new InvalidDataException("Published record requires at least one change.");
        foreach (var change in Changes) ValidateChange(change);
    }

    private static void ValidateVersion(PublishedDocumentVersion? version)
    {
        if (version is null || !PublicationProtocol.IsSha256(version.ExtractionId) || version.Text is null ||
            !Uri.TryCreate(version.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Published document version is invalid.");
    }

    private void ValidateCitation(PublishedCitation? citation)
    {
        if (citation is null) throw new InvalidDataException("Published citation is missing.");
        var text = citation.ExtractionId == Before.ExtractionId ? Before.Text
            : citation.ExtractionId == After.ExtractionId ? After.Text
            : throw new InvalidDataException("Published citation must cite one of the record's versions.");
        if (citation.Length <= 0 || !IsRange(text, citation.Start, citation.Length) ||
            !string.Equals(text.Substring(citation.Start, citation.Length), citation.Quote, StringComparison.Ordinal))
            throw new InvalidDataException("Published citation quote does not match its offsets.");
    }

    private void ValidateLabels()
    {
        if (Officials is null || IssueIds is null || Officials.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name)) ||
            IssueIds.Any(string.IsNullOrWhiteSpace) ||
            Officials.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Officials.Length ||
            IssueIds.Distinct(StringComparer.Ordinal).Count() != IssueIds.Length)
            throw new InvalidDataException("Published record officials or issues are invalid.");
    }

    private void ValidateChange(PublishedChange? change)
    {
        if (change is null || change.WordEdits is null ||
            !IsRange(Before.Text, change.BeforeStart, change.BeforeLength) ||
            !IsRange(After.Text, change.AfterStart, change.AfterLength) ||
            !IsRange(Before.Text, change.BeforeContextStart, change.BeforeContextLength) ||
            !IsRange(After.Text, change.AfterContextStart, change.AfterContextLength) ||
            change.WordEdits.Any(edit => edit is null || !IsRange(Before.Text, edit.BeforeStart, edit.BeforeLength) ||
                !IsRange(After.Text, edit.AfterStart, edit.AfterLength)))
            throw new InvalidDataException("Published change offsets are outside their texts.");
    }

    private static bool IsRange(string text, int start, int length) =>
        start >= 0 && length >= 0 && start <= text.Length - length;
}
