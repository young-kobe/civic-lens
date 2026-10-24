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
    public required PublishedCorrection[] Corrections { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.DocumentChange,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = Officials,
        IssueIds = IssueIds,
        PolicyAreaIds = [],
        SourceHost = After.Host,
        EventDate = ChangeDate
    };

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.DocumentChange, RecordId) || RevisionNumber < 1 || string.IsNullOrWhiteSpace(Headline) ||
            string.IsNullOrWhiteSpace(Summary) || string.IsNullOrWhiteSpace(Institution))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        PublishedEvidenceRules.RequireVersion(Before);
        PublishedEvidenceRules.RequireVersion(After);
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

    private void ValidateCitation(PublishedCitation? citation)
    {
        if (citation is null) throw new InvalidDataException("Published citation is missing.");
        var text = citation.ExtractionId == Before.ExtractionId ? Before.Text
            : citation.ExtractionId == After.ExtractionId ? After.Text
            : throw new InvalidDataException("Published citation must cite one of the record's versions.");
        PublishedEvidenceRules.RequireQuote(text, citation.Start, citation.Length, citation.Quote);
    }

    private void ValidateLabels()
    {
        if (Officials is null || IssueIds is null)
            throw new InvalidDataException("Published record officials or issues are missing.");
        if (Officials.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name)))
            throw new InvalidDataException("Published record officials require a name.");
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Officials.Select(item => item.Id)))
            throw new InvalidDataException("Published record official ids must be distinct and not blank.");
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(IssueIds))
            throw new InvalidDataException("Published record issue ids must be distinct and not blank.");
    }

    private void ValidateChange(PublishedChange? change)
    {
        if (change is null || change.WordEdits is null ||
            !PublishedEvidenceRules.IsRange(Before.Text, change.BeforeStart, change.BeforeLength) ||
            !PublishedEvidenceRules.IsRange(After.Text, change.AfterStart, change.AfterLength) ||
            !PublishedEvidenceRules.IsRange(Before.Text, change.BeforeContextStart, change.BeforeContextLength) ||
            !PublishedEvidenceRules.IsRange(After.Text, change.AfterContextStart, change.AfterContextLength) ||
            change.WordEdits.Any(edit => edit is null || !PublishedEvidenceRules.IsRange(Before.Text, edit.BeforeStart, edit.BeforeLength) ||
                !PublishedEvidenceRules.IsRange(After.Text, edit.AfterStart, edit.AfterLength)))
            throw new InvalidDataException("Published change offsets are outside their texts.");
    }
}
