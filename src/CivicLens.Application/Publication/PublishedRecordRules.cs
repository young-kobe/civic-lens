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
        decisions
            .Where(item => item.RevisionNumber == revisionNumber && item.Kind == ReviewDecisionKind.Approve)
            .MaxBy(item => item.ReviewStateVersion)!.CreatedAtUtc;

    public static PublishedCitation Cite(SourceSpan statementSpan, DocumentExtraction extraction)
    {
        var span = new DocumentTextSpan(extraction, statementSpan.Start, statementSpan.Length);
        return new() { ExtractionId = span.ExtractionId, Start = span.Start, Length = span.Length, Quote = span.Quote };
    }

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
