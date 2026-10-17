using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class VoteRecordLoader(IVoteReviewStore votes)
    : PublishedRecordLoader<UnpublishedApprovedVote, PublishableVote>(PublishedRecordKind.Vote, "Vote")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedVote>> ListCandidatesAsync(int limit,
        CancellationToken cancellationToken) => votes.ListUnpublishedApprovedAsync(limit, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedVote candidate) =>
        Ready(candidate.Vote.CurrentStatus, candidate.Vote.UnresolvedConcerns.Length, candidate.Vote.RecordId,
            candidate.Vote.CurrentRevision.Headline, candidate.Vote.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableVote>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        votes.GetForPublicationAsync(recordIds, cancellationToken);

    protected override async Task<PendingRecord> BuildAsync(string recordId, PublishableVote addition,
        PublicationBuild build, CancellationToken cancellationToken)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedVoteBuilder(build.Rules).Build(review.CurrentRevision, review.Decisions, addition.Extractions,
            build.Previous.Find(Kind, recordId), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            await build.Renderer.RenderVoteAsync(record, BillActionRecordIn(addition, build), cancellationToken));
    }

    private static string? BillActionRecordIn(PublishableVote addition, PublicationBuild build) =>
        addition.BillActionRecordIds.FirstOrDefault(id =>
            build.ReleaseRecords.Any(record => record.Kind == PublishedRecordKind.Action && record.RecordId == id));
}
