namespace CivicLens.Publication.Contracts;

public sealed record PublishedDocumentChange
{
    public const int MaximumChanges = 2_000;
    public const int MaximumWordEditsPerChange = 512;
    public const int MaximumCitations = 64;
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
        ValidateContent(Before, After, Citations, Changes, ChangeDate, ChangeDateEvidence);
        ValidateLabels();
    }

    public static void ValidateContent(PublishedDocumentVersion before, PublishedDocumentVersion after,
        PublishedCitation[] citations, PublishedChange[] changes, DateOnly? changeDate, PublishedCitation? changeDateEvidence)
    {
        PublishedEvidenceRules.RequireVersion(before);
        PublishedEvidenceRules.RequireVersion(after);
        if (before.ExtractionId == after.ExtractionId)
            throw new InvalidDataException("Published record versions must differ.");
        if (citations is null || citations.Length is 0 or > MaximumCitations)
            throw new InvalidDataException($"Published record requires 1 to {MaximumCitations} citations.");
        foreach (var citation in citations) ValidateCitation(before, after, citation);
        if ((changeDate is null) != (changeDateEvidence is null))
            throw new InvalidDataException("An actual change date requires exact evidence.");
        if (changeDateEvidence is not null) ValidateCitation(before, after, changeDateEvidence);
        if (changes is null || changes.Length is 0 or > MaximumChanges)
            throw new InvalidDataException($"Published record requires 1 to {MaximumChanges} changes.");
        foreach (var change in changes) ValidateChange(before, after, change);
    }

    private static void ValidateCitation(PublishedDocumentVersion before, PublishedDocumentVersion after, PublishedCitation? citation)
    {
        if (citation is null) throw new InvalidDataException("Published citation is missing.");
        var text = citation.ExtractionId == before.ExtractionId ? before.Text
            : citation.ExtractionId == after.ExtractionId ? after.Text
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

    private static void ValidateChange(PublishedDocumentVersion before, PublishedDocumentVersion after, PublishedChange? change)
    {
        if (change is null || change.WordEdits is null || change.WordEdits.Length > MaximumWordEditsPerChange ||
            !PublishedEvidenceRules.IsRange(before.Text, change.BeforeStart, change.BeforeLength) ||
            !PublishedEvidenceRules.IsRange(after.Text, change.AfterStart, change.AfterLength) ||
            !PublishedEvidenceRules.IsRange(before.Text, change.BeforeContextStart, change.BeforeContextLength) ||
            !PublishedEvidenceRules.IsRange(after.Text, change.AfterContextStart, change.AfterContextLength) ||
            change.WordEdits.Any(edit => edit is null || !PublishedEvidenceRules.IsRange(before.Text, edit.BeforeStart, edit.BeforeLength) ||
                !PublishedEvidenceRules.IsRange(after.Text, edit.AfterStart, edit.AfterLength)))
            throw new InvalidDataException("Published change offsets are outside their texts.");
        ValidateWordEdits(change);
    }

    private static void ValidateWordEdits(PublishedChange change)
    {
        var beforeCursor = change.BeforeStart;
        var afterCursor = change.AfterStart;
        var beforeEnd = change.BeforeStart + change.BeforeLength;
        var afterEnd = change.AfterStart + change.AfterLength;
        foreach (var edit in change.WordEdits)
        {
            if (edit.BeforeStart < beforeCursor || edit.AfterStart < afterCursor ||
                edit.BeforeLength > beforeEnd - edit.BeforeStart || edit.AfterLength > afterEnd - edit.AfterStart)
                throw new InvalidDataException("Published word edits must be ordered, nonoverlapping, and inside their change.");
            beforeCursor = edit.BeforeStart + edit.BeforeLength;
            afterCursor = edit.AfterStart + edit.AfterLength;
        }
    }
}
