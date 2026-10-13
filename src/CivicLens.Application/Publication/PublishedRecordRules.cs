using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedRecordRules(PublicationCatalog catalog)
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
}
