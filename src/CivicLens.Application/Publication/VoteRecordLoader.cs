using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class VoteRecordLoader(IVoteReviewStore votes)
    : PublishedRecordLoader<UnpublishedApprovedVote, VoteRecordLoader.VoteAddition>(PublishedRecordKind.Vote, "Vote")
{
    internal sealed record VoteAddition(PublishableVote Vote, VoteParties Parties);

    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedVote>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        votes.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override string RecordIdOf(UnpublishedApprovedVote candidate) => candidate.Vote.RecordId;

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedVote candidate) =>
        Ready(candidate.Vote.CurrentStatus, candidate.Vote.UnresolvedConcerns.Length, candidate.Vote.RecordId,
            candidate.Vote.CurrentRevision.RevisionNumber);

    protected override async Task<IReadOnlyDictionary<string, VoteAddition>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken)
    {
        var found = await votes.GetForPublicationAsync(recordIds, cancellationToken);
        var roster = new VoteParties(build.Roster);
        return found.ToDictionary(item => item.Key, item => new VoteAddition(item.Value, roster.Pinned(item.Value.StoredParties)), StringComparer.Ordinal);
    }

    protected override PendingRecord Build(string recordId, VoteAddition addition, PublicationBuild build)
    {
        var review = addition.Vote.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build);
        var built = new PublishedVoteBuilder(build.Rules).Build(review.CurrentRevision, review.Decisions, addition.Vote.Extractions,
            build.FirstPublishedAt(Kind, recordId), [], build.Now, addition.Parties);
        var textRewritten = build.Corrections.EarlierVote(recordId) is { } earlier && built.IsTextRewriteOf(earlier);
        var record = built with
        {
            Corrections = build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions, textRewritten)
        };
        PublishedCorrection.ValidateAll(record.Corrections, record.RevisionNumber);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record, record.Corrections,
            (related, billLines, cancellationToken) => build.Renderer.RenderVoteAsync(record, billLines.BillOfVote(recordId), related, build.Catalog, cancellationToken));
    }

    public Task<IReadOnlyList<VoteMembership>> MembersAsync(IEnumerable<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        votes.ReadMembersAsync([.. entries.Where(entry => entry.Kind == Kind).Select(PublishedRevisionBinding.Of)],
            cancellationToken);

    public Task<IReadOnlyDictionary<string, ImmutableArray<string>>> BillActionRecordIdsAsync(ImmutableArray<PublishedRevisionBinding> revisions,
        CancellationToken cancellationToken) =>
        votes.FindBillActionRecordIdsOfVotesAsync(revisions, cancellationToken);

    public override Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken) =>
        build.Renderer.RenderVoteAsync(Parse<PublishedVote>(json), build.BillLines.BillOfVote(recordId), links.Citing(Kind, recordId), build.Catalog, cancellationToken);
}
