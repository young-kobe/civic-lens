using System.Text.RegularExpressions;

namespace CivicLens.Publication.Contracts;

public sealed partial record PublishedBrief
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumParagraphLength = 4_000;
    public const int MaximumParagraphs = 12;
    public const int MaximumCitations = 256;

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required DateOnly WeekStart { get; init; }
    public required int CutoffReleaseNumber { get; init; }
    public required PublishedBriefCoverage Coverage { get; init; }
    public required PublishedBriefParagraph[] Paragraphs { get; init; }
    public required PublishedBriefCitation[] Citations { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedCorrection[] Corrections { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Brief,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        EventDate = WeekStart
    };

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Brief, RecordId) || RevisionNumber < 1 ||
            WeekStart.DayOfWeek != DayOfWeek.Monday || CutoffReleaseNumber < 1)
            throw new InvalidDataException("Published brief identity or week is invalid.");
        RequireText(Headline, MaximumHeadlineLength);
        ValidateCitations();
        ValidateParagraphs();
        if (Coverage is null || Coverage.Cited != Citations.Length || Coverage.Considered < Coverage.SentToModel)
            throw new InvalidDataException("Published brief coverage must count the listed citations.");
    }

    private void ValidateCitations()
    {
        if (Citations is null || Citations.Length is 0 or > MaximumCitations)
            throw new InvalidDataException($"Published brief requires 1 to {MaximumCitations} citations.");
        foreach (var citation in Citations)
        {
            if (citation is null || !CitationId().IsMatch(citation.Id) || citation.Kind == PublishedRecordKind.Brief ||
                !PublicationProtocol.IsRecordId(citation.Kind, citation.RecordId) || citation.RevisionNumber < 1)
                throw new InvalidDataException("Published brief citation needs an ID, a cited kind, a record, and a revision.");
            RequireText(citation.Headline, MaximumHeadlineLength);
        }
        if (Citations.Select(citation => citation.Id).Distinct(StringComparer.Ordinal).Count() != Citations.Length ||
            Citations.Select(citation => (citation.Kind, citation.RecordId)).Distinct().Count() != Citations.Length)
            throw new InvalidDataException("Published brief cites each record once, under its own ID.");
    }

    private void ValidateParagraphs()
    {
        if (Paragraphs is null || Paragraphs.Length is 0 or > MaximumParagraphs)
            throw new InvalidDataException($"Published brief requires 1 to {MaximumParagraphs} paragraphs.");
        var listed = Citations.Select(citation => citation.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var paragraph in Paragraphs)
        {
            if (paragraph is null) throw new InvalidDataException("Published brief paragraph is missing.");
            RequireText(paragraph.Text, MaximumParagraphLength);
            if (paragraph.CitationIds is not { Length: > 0 } ids || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new InvalidDataException("Each published brief paragraph cites 1 or more distinct IDs.");
            if (!ids.All(listed.Contains))
                throw new InvalidDataException("A published brief paragraph cites an ID that the brief does not list.");
        }
        var used = Paragraphs.SelectMany(paragraph => paragraph.CitationIds).ToHashSet(StringComparer.Ordinal);
        if (!listed.All(used.Contains))
            throw new InvalidDataException("Every published brief citation is cited in a paragraph.");
    }

    private static void RequireText(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength)
            throw new InvalidDataException($"Published brief text must be 1 to {maximumLength} characters.");
    }

    [GeneratedRegex(@"^R[1-9][0-9]{0,3}$")]
    private static partial Regex CitationId();
}
