using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedRecordBuilder(PublicationCatalog catalog)
{
    public static bool IsPublishable(ReviewStatus status, int unresolvedConcernCount) =>
        status == ReviewStatus.Approved && unresolvedConcernCount == 0;

    public static void RequirePublishable(PublishedRecordKind kind, string recordId, int revisionNumber,
        ImmutableArray<ReviewDecision> decisions, ImmutableArray<ReviewDecision> unresolvedConcerns)
    {
        var status = ReviewPolicy.GetCurrentStatus(revisionNumber, decisions, unresolvedConcerns);
        if (!IsPublishable(status, unresolvedConcerns.Length))
            throw new ArgumentException($"{Noun(kind)} {recordId} is not approved or has unresolved concerns.");
    }

    public static void RequireNotPublishedAt(PublishedRecordKind kind, string recordId, int revisionNumber, PreviousRelease previous)
    {
        if (previous.Find(kind, recordId)?.RevisionNumber == revisionNumber)
            throw new ArgumentException($"{Noun(kind)} {recordId} is already published at its current revision.");
    }

    public static string Noun(PublishedRecordKind kind) => kind == PublishedRecordKind.Statement ? "Statement" : "Draft";

    public static DateTimeOffset GetApprovalTime(int revisionNumber, ImmutableArray<ReviewDecision> decisions) =>
        decisions
            .Where(item => item.RevisionNumber == revisionNumber && item.Kind == ReviewDecisionKind.Approve)
            .MaxBy(item => item.ReviewStateVersion)!.CreatedAtUtc;

    public PublishedOfficial[] MapOfficials(PublishedRecordKind kind, string recordId, IEnumerable<string> officialIds) =>
        [.. officialIds.Select(id => new PublishedOfficial
        {
            Id = id,
            Name = catalog.OfficialNames.TryGetValue(id, out var name)
                ? name
                : throw new ArgumentException($"Official {id} in {Noun(kind).ToLowerInvariant()} {recordId} has no configured public name.")
        })];

    public PublishedDocumentChange Build(PublishableDocumentChange change, PublishedRecordEntry? previous,
        DateTimeOffset now)
    {
        var (review, comparison, before, after) = change;
        var revision = review.CurrentRevision;

        var record = new PublishedDocumentChange
        {
            RecordId = review.DraftId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            Summary = revision.Summary,
            Significance = revision.Significance,
            Limits = revision.Limits,
            Institution = revision.Institution,
            ChangeDate = revision.ChangeDate,
            ChangeDateEvidence = revision.ChangeDateEvidence is null ? null : Cite(revision.ChangeDateEvidence, before, after),
            Officials = MapOfficials(PublishedRecordKind.DocumentChange, review.DraftId, revision.OfficialIds),
            IssueIds = [.. revision.IssueIds],
            Before = new()
            {
                ExtractionId = before.ExtractionId,
                Url = comparison.BeforeFinalUrl,
                ObservedAtUtc = comparison.BeforeObservedAtUtc,
                Text = before.Text
            },
            After = new()
            {
                ExtractionId = after.ExtractionId,
                Url = comparison.AfterFinalUrl,
                ObservedAtUtc = comparison.AfterObservedAtUtc,
                Text = after.Text
            },
            Changes = [.. comparison.Comparison.Hunks.Select(MapChange)],
            Citations = [.. revision.Citations.Select(citation => Cite(citation, before, after))],
            ApprovedAtUtc = GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = previous?.FirstPublishedAtUtc ?? now
        };
        record.Validate();
        return record;
    }

    private static PublishedCitation Cite(DocumentChangeCitation citation, DocumentExtraction before, DocumentExtraction after)
    {
        var extraction = citation.ExtractionId == before.ExtractionId ? before
            : citation.ExtractionId == after.ExtractionId ? after
            : throw new InvalidDataException("A citation names an extraction outside its comparison.");
        var span = new DocumentTextSpan(extraction, citation.Start, citation.Length);
        return new() { ExtractionId = span.ExtractionId, Start = span.Start, Length = span.Length, Quote = span.Quote };
    }

    private static PublishedChange MapChange(DocumentComparisonHunk hunk) => new()
    {
        BeforeStart = hunk.BeforeStart,
        BeforeLength = hunk.BeforeLength,
        AfterStart = hunk.AfterStart,
        AfterLength = hunk.AfterLength,
        BeforeContextStart = hunk.BeforeContextStart,
        BeforeContextLength = hunk.BeforeContext.Length,
        AfterContextStart = hunk.AfterContextStart,
        AfterContextLength = hunk.AfterContext.Length,
        WordEdits = [.. hunk.WordEdits.Select(edit => new PublishedWordEdit
        {
            BeforeStart = edit.BeforeStart,
            BeforeLength = edit.BeforeLength,
            AfterStart = edit.AfterStart,
            AfterLength = edit.AfterLength
        })]
    };
}
