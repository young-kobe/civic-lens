using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private static readonly string SuggestionRunId = new('7', 32);
    private static readonly JsonSerializerOptions ReaderJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EveryReaderOfAFullRevisionReturnsTheRevisionThatWasWrittenBecauseTheColumnsAreTheOnlyCopy()
    {
        var (recordId, candidate, written) = await SettledFullRevisionAsync();
        var stored = JsonSerializer.SerializeToNode(written, ReaderJson);
        var reviews = VoteReviews();

        var found = await reviews.GetRevisionAsync(recordId, 1, default);
        var review = await reviews.GetAsync(recordId, default);
        var evidence = await VoteStore().ReadEvidenceAsync(candidate, default);
        var publishable = await reviews.GetForPublicationAsync([recordId], default);
        var members = await reviews.ReadMembersAsync([new(PublishedRecordKind.Vote, recordId, 1)], default);

        foreach (var read in new[] { found, review!.Review.CurrentRevision, review.Review.LastApprovedRevision, evidence!.Current, publishable[recordId].Review.CurrentRevision })
            Assert.True(JsonNode.DeepEquals(stored, JsonSerializer.SerializeToNode(read, ReaderJson)), "A reader returned a revision that differs from the written revision.");
        Assert.Equal(written.Members.Select(member => member.PersonId).Distinct().Order(), members.Select(member => member.PersonId).Order());
    }

    [Fact]
    public async Task ARevisionKeepsItsFactsAndIssuesWhenAWriteCommitsBetweenTheParentReadAndTheChildReadsBecauseTheChildRowsComeByExactKeys()
    {
        var (recordId, candidate, written) = await SettledFullRevisionAsync();
        var racing = new WriteAfterFirstVoteRevisionRead(async () => await SaveAsync(recordId, 1, SuggestionChoice.Keep, "save-race"));
        var store = new PostgresVoteDraftStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(racing).Options));

        var evidence = await store.ReadEvidenceAsync(candidate, default);

        Assert.True(racing.Fired, "The write did not run between the reads.");
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync("SELECT current_revision_number FROM vote_drafts")));
        Assert.Equal(1, evidence!.Current!.RevisionNumber);
        Assert.Equal(written.Facts.Select(fact => fact.Text), evidence.Current.Facts.Select(fact => fact.Text));
        Assert.Equal([.. written.IssueCandidateIds], [.. evidence.Current.IssueCandidateIds]);
        Assert.Equal([.. written.TrackedIssueIds], [.. evidence.Current.TrackedIssueIds]);
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
            SubjectsHash = new string('5', 64),
            Suggestion = new("Suggested headline", "Suggested summary.", SuggestionRunId, new string('6', 64))
        };
        await InsertRunAsync(candidate, VoteDraftTask.PromptVersion, 1, "Bypassed", SuggestionRunId);
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

    private sealed class WriteAfterFirstVoteRevisionRead(Func<Task> write) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Fired || !command.CommandText.Contains("vote_revisions", StringComparison.Ordinal)) return result;
            Fired = true;
            await write();
            return result;
        }
    }

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
