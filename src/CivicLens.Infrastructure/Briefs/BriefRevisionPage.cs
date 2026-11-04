using System.Collections.Immutable;
using CivicLens.Core.Briefs;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Briefs;

/// <summary>Brief revision rows with their citation rows, paragraph rows, and paragraph citation rows. A page costs a fixed number of queries.</summary>
internal sealed record BriefRevisionPage(IReadOnlyList<BriefRevisionRow> Rows,
    ILookup<(string RecordId, int RevisionNumber), BriefRevisionCitationRow> Citations,
    ILookup<(string RecordId, int RevisionNumber), BriefRevisionParagraphRow> Paragraphs,
    ILookup<(string RecordId, int RevisionNumber), BriefRevisionParagraphCitationRow> ParagraphCitations) : IRevisionRowPage<BriefRevisionRow>
{
    /// <summary>Rebuilds the revision of a row. A row with null columns fails here, and the JSON is no fallback.</summary>
    public BriefRevision Rebuild(BriefRevisionRow row)
    {
        if (row.Headline is null)
            throw BriefRevisionRows.NotBackfilled($"Brief revision {row.RecordId} {row.RevisionNumber}");
        var citations = CitationsOf(row, BriefRevisionPart.Text);
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            new(row.WeekStart!.Value, row.WindowStartUtcTicks!.Value, row.WindowEndUtcTicks!.Value, row.FirstReleaseNumber!.Value, row.CutoffReleaseNumber!.Value),
            row.Fingerprint!, row.Headline, row.TextOrigin!.Value, SuggestionOf(row), ParagraphsOf(row, BriefRevisionPart.Text), citations,
            new(row.CoverageConsidered!.Value, row.CoverageSentToModel!.Value, citations.Length));
    }

    public IReadOnlyList<StoredRevision<BriefRevisionRow, BriefRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<BriefRevisionRow, BriefRevision>(row, () => Rebuild(row)))];

    private BriefTextSuggestion? SuggestionOf(BriefRevisionRow row) => row.SuggestionHeadline is null
        ? null
        : new(row.SuggestionHeadline, ParagraphsOf(row, BriefRevisionPart.Suggestion), CitationsOf(row, BriefRevisionPart.Suggestion),
            row.SuggestionRunId!, row.SuggestionFingerprint!);

    private ImmutableArray<BriefCitation> CitationsOf(BriefRevisionRow row, BriefRevisionPart part) =>
        [.. Citations[(row.RecordId, row.RevisionNumber)].Where(citation => citation.Part == part).OrderBy(citation => citation.Position).Select(CitationOf)];

    private ImmutableArray<BriefParagraph> ParagraphsOf(BriefRevisionRow row, BriefRevisionPart part)
    {
        var links = ParagraphCitations[(row.RecordId, row.RevisionNumber)].Where(link => link.Part == part).ToLookup(link => link.ParagraphPosition);
        return [.. Paragraphs[(row.RecordId, row.RevisionNumber)].Where(paragraph => paragraph.Part == part).OrderBy(paragraph => paragraph.Position)
            .Select(paragraph => new BriefParagraph(paragraph.ParagraphText,
                [.. links[paragraph.Position].OrderBy(link => link.Position).Select(link => link.CitationId)]))];
    }

    private static BriefCitation CitationOf(BriefRevisionCitationRow citation) => citation.Kind switch
    {
        BriefCitationKind.Statement => new(citation.CitationId, citation.Kind, citation.StatementCandidateId!, citation.StatementRevisionNumber!.Value),
        BriefCitationKind.Action => new(citation.CitationId, citation.Kind, citation.ActionRecordId!, citation.ActionRevisionNumber!.Value),
        _ => new(citation.CitationId, citation.Kind, citation.DocumentChangeDraftId!, citation.DocumentChangeRevisionNumber!.Value)
    };
}
