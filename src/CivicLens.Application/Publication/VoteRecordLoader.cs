using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class VoteRecordLoader(IVoteReviewStore votes)
    : PublishedRecordLoader<UnpublishedApprovedVote, VoteRecordLoader.VoteAddition>(PublishedRecordKind.Vote, "Vote"),
      ICarriedPageLoader
{
    internal sealed record VoteAddition(PublishableVote Vote, VoteParties Parties);

    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedVote>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        votes.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedVote candidate) =>
        Ready(candidate.Vote.CurrentStatus, candidate.Vote.UnresolvedConcerns.Length, candidate.Vote.RecordId,
            candidate.Vote.CurrentRevision.Headline, candidate.Vote.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber);

    protected override async Task<IReadOnlyDictionary<string, VoteAddition>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken)
    {
        var found = await votes.GetForPublicationAsync(recordIds, cancellationToken);
        var parties = new VoteParties(build.Roster);
        return found.ToDictionary(item => item.Key, item => new VoteAddition(item.Value, parties), StringComparer.Ordinal);
    }

    protected override PendingRecord Build(string recordId, VoteAddition addition, PublicationBuild build)
    {
        var review = addition.Vote.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedVoteBuilder(build.Rules).Build(review.CurrentRevision, review.Decisions, addition.Vote.Extractions,
            build.Previous.FirstPublishedAt(Kind, recordId), build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions), build.Now, addition.Parties);
        var billActionRecordId = BillActionRecordIn(addition.Vote.BillActionRecordIds, build);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (related, cancellationToken) => build.Renderer.RenderVoteAsync(record, billActionRecordId, related, build.Catalog, cancellationToken));
    }

    public Task<IReadOnlyList<VoteMembership>> MembersAsync(IEnumerable<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        votes.ReadMembersAsync([.. entries.Where(entry => entry.Kind == Kind).Select(PublishedRevisionBinding.Of)],
            cancellationToken);

    public async Task<IReadOnlySet<string>> FindOnBillsOfActionsAsync(IEnumerable<string> actionRecordIds, CancellationToken cancellationToken)
    {
        ImmutableArray<string> ids = [.. actionRecordIds];
        return ids.IsEmpty ? new HashSet<string>() : await votes.FindVoteRecordIdsOnBillsOfActionsAsync(ids, cancellationToken);
    }

    public async Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken)
    {
        var vote = Parse<PublishedVote>(json);
        var billActionRecordIds = VoteBill.Of(vote) is { } bill
            ? (await votes.FindBillActionRecordIdsAsync([bill], cancellationToken)).GetValueOrDefault(recordId, [])
            : [];
        return await build.Renderer.RenderVoteAsync(vote, BillActionRecordIn(billActionRecordIds, build), links.Citing(Kind, recordId),
            build.Catalog, cancellationToken);
    }

    private static string? BillActionRecordIn(ImmutableArray<string> billActionRecordIds, PublicationBuild build) =>
        billActionRecordIds.FirstOrDefault(id =>
            build.ReleaseRecords.Any(record => record.Kind == PublishedRecordKind.Action && record.RecordId == id));
}
