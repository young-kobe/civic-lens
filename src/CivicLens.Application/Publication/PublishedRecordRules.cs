using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedRecordRules(PublicationCatalog catalog)
{
    public static bool IsPublishable(ReviewStatus status, int unresolvedConcernCount) =>
        status == ReviewStatus.Approved && unresolvedConcernCount == 0;

    public static DateTimeOffset GetApprovalTime(int revisionNumber, ImmutableArray<ReviewDecision> decisions) =>
        ApprovalOf(revisionNumber, decisions)!.CreatedAtUtc;

    public static bool IsApprovedByCode(int revisionNumber, ImmutableArray<ReviewDecision> decisions) =>
        ReviewAuthor.IsCode(ApprovalOf(revisionNumber, decisions)?.ActorSubject);

    private static ReviewDecision? ApprovalOf(int revisionNumber, ImmutableArray<ReviewDecision> decisions) =>
        decisions
            .Where(item => item.RevisionNumber == revisionNumber && item.Kind == ReviewDecisionKind.Approve)
            .MaxBy(item => item.ReviewStateVersion);

    public static PublishedTextOrigin TextOriginOf(ReviewTextOrigin origin) =>
        origin == ReviewTextOrigin.Person ? PublishedTextOrigin.Person : PublishedTextOrigin.Model;

    public static PublishedCitation Cite(SourceSpan source, DocumentExtraction extraction)
    {
        var span = new DocumentTextSpan(extraction, source.Start, source.Length);
        return new() { ExtractionId = span.ExtractionId, Start = span.Start, Length = span.Length, Quote = span.Quote };
    }

    public static PublishedDocumentVersion[] Sources(IEnumerable<SourceSpan> citations, IReadOnlyDictionary<string, DocumentExtraction> extractions) =>
        [.. citations.Select(span => span.ExtractionId).Distinct(StringComparer.Ordinal)
            .Select(id => extractions[id])
            .Select(extraction => new PublishedDocumentVersion
            {
                ExtractionId = extraction.ExtractionId,
                Url = extraction.SourceAttempt.FinalUrl,
                ObservedAtUtc = extraction.SourceAttempt.ObservedAt,
                Text = extraction.Text
            })];

    public PublishedOfficial MapOfficial(string recordId, string officialId) => new()
    {
        Id = officialId,
        Name = catalog.OfficialNames.TryGetValue(officialId, out var name)
            ? name
            : throw new ArgumentException($"Official {officialId} in record {recordId} has no configured public name.")
    };

    public PublishedOfficial[] MapOfficials(string recordId, IEnumerable<string> officialIds) =>
        [.. officialIds.Select(id => MapOfficial(recordId, id))];
}
