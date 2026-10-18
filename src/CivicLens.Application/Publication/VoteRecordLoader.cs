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

    protected override PendingRecord Build(string recordId, PublishableVote addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedVoteBuilder(build.Rules).Build(review.CurrentRevision, review.Decisions, addition.Extractions,
            build.Previous.Find(Kind, recordId), build.Now);
        var billActionRecordId = BillActionRecordIn(addition.BillActionRecordIds, build);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (related, cancellationToken) => build.Renderer.RenderVoteAsync(record, billActionRecordId, related, cancellationToken));
    }

    public override async Task<IReadOnlyDictionary<string, string>> RenderCarriedAsync(IReadOnlyDictionary<string, byte[]> records,
        ReleaseLinks links, PublicationBuild build, CancellationToken cancellationToken)
    {
        var parsed = ParseAll<PublishedVote>(records);
        var billActionRecordIds = await votes.FindBillActionRecordIdsAsync(
            [.. parsed.Values.Where(vote => vote.BillType is not null)
                .Select(vote => new VoteBill(vote.RecordId, vote.Congress, vote.BillType!, vote.BillNumber!.Value))],
            cancellationToken);
        return await RenderEachAsync(parsed, (vote, recordId) => build.Renderer.RenderVoteAsync(vote,
            BillActionRecordIn(billActionRecordIds.GetValueOrDefault(recordId, []), build), links.Citing(Kind, recordId), cancellationToken));
    }

    private static string? BillActionRecordIn(ImmutableArray<string> billActionRecordIds, PublicationBuild build) =>
        billActionRecordIds.FirstOrDefault(id =>
            build.ReleaseRecords.Any(record => record.Kind == PublishedRecordKind.Action && record.RecordId == id));
}
