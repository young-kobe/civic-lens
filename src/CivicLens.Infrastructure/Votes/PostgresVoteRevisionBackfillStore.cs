using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

public sealed class PostgresVoteRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string UnprovenSource = "UnprovenSource";

    private readonly RevisionBackfill<VoteRevisionRow, VoteDraftRevision, VoteRevisionPage> backfill = new(contextFactory, new(
        "vote_revision_backfill", PostgresAdvisoryLock.VoteReview, row => row.Headline == null, VoteRevisionRows.ReadPageAsync, FillColumnsAsync,
        ReadMovable, DifferingFields));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static async Task FillColumnsAsync(CollectionAttemptDbContext db, VoteRevisionRow row, VoteDraftRevision revision, CancellationToken cancellationToken)
    {
        VoteRevisionRows.FillColumns(row, revision);
        await VoteMemberLists.EnsureAsync(db, revision, cancellationToken);
    }

    private static (VoteDraftRevision? Revision, string? Refusal) ReadMovable(VoteRevisionRow row)
    {
        if (RevisionJsonComparison.TryParseObject(row.RevisionJson) is not { } stored) return (null, RevisionBackfillReasons.UnreadableJson);
        VoteDraftRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<VoteDraftRevision>(row.RevisionJson, "vote revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
        if (!stored.ContainsKey(VoteRevisionComparison.SourceKey))
        {
            if (!VoteRevisionComparison.MayOmitSource(stored)) return (null, UnprovenSource);
            revision = revision with { Source = VoteSource.CongressGov };
        }
        if (HasNullElements(revision)) return (null, RevisionBackfillReasons.InvalidShape);
        try
        {
            revision.ValidateShape();
        }
        catch (ArgumentException)
        {
            return (null, RevisionBackfillReasons.InvalidShape);
        }
        if (VoteFacts.Find(revision.Facts, VoteFactPrefixes.Result) is null) return (null, RevisionBackfillReasons.InvalidShape);
        if (RevisionBackfillReasons.HasDuplicates(revision.TrackedIssueIds) || !revision.IssueCandidateIds.IsDefault && RevisionBackfillReasons.HasDuplicates(revision.IssueCandidateIds))
            return (null, RevisionBackfillReasons.DuplicateIssueId);
        return (revision, null);
    }

    private static bool HasNullElements(VoteDraftRevision revision) =>
        !revision.Facts.IsDefault && revision.Facts.Any(fact => fact is null) || !revision.Members.IsDefault && revision.Members.Any(member => member is null);

    private static IReadOnlyList<string> DifferingFields(VoteRevisionRow row, VoteRevisionPage page)
    {
        var members = page.MembersOf(row);
        var fields = VoteRevisionComparison.DifferingKeys(row.RevisionJson, VoteRevisionRows.Rebuild(row, members, page.FactsOf(row), page.IssuesOf(row))).ToList();
        if (row.MemberListId != VoteMemberLists.IdOf(members)) fields.Add("memberListId");
        if (page.MemberCounts.GetValueOrDefault(row.MemberListId!) != members.Length) fields.Add("memberCount");
        return fields;
    }
}
