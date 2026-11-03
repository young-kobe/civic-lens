using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Votes;
using System.Data.Common;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private static readonly JsonSerializerOptions ReaderJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EveryReaderOfAFullRevisionReturnsTheRevisionThatItsJsonHoldsBecauseTheReadersMovedToColumns()
    {
        var (recordId, candidate, written) = await SettledFullRevisionAsync();
        var stored = JsonNode.Parse((string)(await ScalarAsync("SELECT revision_json FROM vote_revisions"))!)!;
        var reviews = VoteReviews();

        var found = await reviews.GetRevisionAsync(recordId, 1, default);
        var review = await reviews.GetAsync(recordId, default);
        var evidence = await VoteStore().ReadEvidenceAsync(candidate, default);
        var publishable = await reviews.GetForPublicationAsync([recordId], default);
        var members = await reviews.ReadMembersAsync([new(PublishedRecordKind.Vote, recordId, 1)], default);

        Assert.True(JsonNode.DeepEquals(stored, JsonSerializer.SerializeToNode(written, ReaderJson)), "The written revision differs from its JSON.");
        foreach (var read in new[] { found, review!.Review.CurrentRevision, review.Review.LastApprovedRevision, evidence!.Current, publishable[recordId].Review.CurrentRevision })
            Assert.True(JsonNode.DeepEquals(stored, JsonSerializer.SerializeToNode(read, ReaderJson)), "A reader returned a revision that differs from the JSON.");
        Assert.Equal(written.Members.Select(member => member.PersonId).Distinct().Order(), members.Select(member => member.PersonId).Order());
    }

    [Fact]
    public async Task ARevisionReadsBackThroughItsColumnsWhenItsJsonIsUnreadableBecauseNoReaderUsesTheJson()
    {
        var (recordId, candidate, written) = await SettledFullRevisionAsync();
        var expected = JsonSerializer.SerializeToNode(written, ReaderJson);
        await ExecuteAsync("UPDATE vote_revisions SET revision_json = '{broken'");
        var reviews = VoteReviews();

        var found = await reviews.GetRevisionAsync(recordId, 1, default);
        var review = await reviews.GetAsync(recordId, default);
        var evidence = await VoteStore().ReadEvidenceAsync(candidate, default);

        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(found, ReaderJson)));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(review!.Review.CurrentRevision, ReaderJson)));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(evidence!.Current, ReaderJson)));
    }

    [Fact]
    public async Task EveryReaderFailsOnARowWithNullColumnsAndNeverFallsBackToTheJsonBecauseASilentFallbackWouldHideAnUnmovedRow()
    {
        var (recordId, candidate, _) = await SettledFullRevisionAsync();
        await ExecuteAsync("UPDATE vote_analysis_runs SET prompt_version = 'vote-draft-prompt-v1'");
        await MakeLegacyAsync(1);
        var reviews = VoteReviews();
        var named = new PublishedRevisionBinding[] { new(PublishedRecordKind.Vote, recordId, 1) };

        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetRevisionAsync(recordId, 1, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetAsync(recordId, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => VoteStore().ReadEvidenceAsync(candidate, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetForPublicationAsync([recordId], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.ReadMembersAsync([.. named], default));
        await Assert.ThrowsAnyAsync<Exception>(() => reviews.FindBillActionRecordIdsOfVotesAsync([.. named], default));
        await Assert.ThrowsAnyAsync<Exception>(() => new PostgresReviewQueueStore(QueueFactory()).SearchApprovedAsync("Headline", null, 10, default));
        await Assert.ThrowsAnyAsync<Exception>(() => VoteStore().RequeueOlderPromptAsync(default));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_revisions WHERE headline IS NULL"));
    }

    [Fact]
    public async Task TheQueriesOfAHistoryReadStayTheSameWhenMoreVotesAreReadBecauseNoReaderQueriesEachRow()
    {
        var counter = new ReaderQueryCounter();
        var counted = new PostgresVoteReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options));
        var first = await SettledVoteAsync(roll: 100);
        await counted.GetForPublicationAsync([first], default);
        counter.Count = 0;
        await counted.GetForPublicationAsync([first], default);
        var queriesForOne = counter.Count;
        var more = new List<string> { first };
        foreach (var roll in new[] { 101, 102, 103 }) more.Add(await SettledVoteAsync(roll));
        counter.Count = 0;

        var read = await counted.GetForPublicationAsync([.. more], default);

        Assert.Equal(4, read.Count);
        Assert.Equal(queriesForOne, counter.Count);
    }

    private async Task<(string RecordId, string Candidate, VoteDraftRevision Revision)> SettledFullRevisionAsync()
    {
        var candidate = await PendingCandidateAsync();
        var basis = await RevisionAsync(candidate, 1);
        var span = new SourceSpan(basis.Facts[0].Citation.ExtractionId, 2, 3);
        var full = basis with
        {
            Kind = VoteKind.Passage,
            KindCitation = span,
            Facts = [basis.Facts[0], new("Second fact", span)],
            Members = [basis.Members[0], new("with-id-b", "B000002", "Nay", span), new("with-id-c", "C000003", "Not Voting", span)],
            IssueCandidateIds = ["issue-a", "issue-b", "issue-c"],
            TrackedIssueIds = ["issue-b"],
            SubjectsHash = new string('5', 64)
        };
        await SettleAsync(candidate, full);
        return (full.RecordId, candidate, full);
    }

    private async Task<string> SettledVoteAsync(int roll)
    {
        var candidate = await PendingCandidateAsync(Updated.AddDays(roll), roll);
        var revision = await RevisionAsync(candidate, 1);
        await SettleAsync(candidate, revision);
        return revision.RecordId;
    }

    private IDbContextFactory<CollectionAttemptDbContext> QueueFactory() => new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private sealed class ReaderQueryCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
