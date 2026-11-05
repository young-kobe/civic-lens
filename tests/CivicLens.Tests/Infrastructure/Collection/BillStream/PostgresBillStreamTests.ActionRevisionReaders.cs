using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly JsonSerializerOptions ActionReaderJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EveryReaderOfAFullRevisionReturnsTheRevisionThatItsJsonHoldsBecauseTheReadersMovedToColumns()
    {
        var written = await SettleFullRevisionAsync();
        var stored = JsonNode.Parse((string)(await ScalarAsync("SELECT revision_json FROM action_revisions"))!)!;
        var reviews = ActionReviews();

        var found = await reviews.GetRevisionAsync(RecordId, 1, default);
        var review = await reviews.GetAsync(RecordId, default);
        var evidence = await ActionStore().ReadEvidenceAsync(written.CandidateId, false, default);
        var publishable = await reviews.GetForPublicationAsync([RecordId], default);
        var unpublished = Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, [], default));

        Assert.True(JsonNode.DeepEquals(stored, JsonSerializer.SerializeToNode(written, ActionReaderJson)), "The written revision differs from its JSON.");
        foreach (var read in new[] { found, review!.Review.CurrentRevision, review.Review.LastApprovedRevision, evidence!.Current,
            publishable[RecordId].Review.CurrentRevision, unpublished.Action.CurrentRevision })
            Assert.True(JsonNode.DeepEquals(stored, JsonSerializer.SerializeToNode(read, ActionReaderJson)), "A reader returned a revision that differs from the JSON.");
    }

    [Fact]
    public async Task ARevisionReadsBackThroughItsColumnsWhenItsJsonIsUnreadableBecauseNoReaderUsesTheJson()
    {
        var written = await SettleFullRevisionAsync();
        var expected = JsonSerializer.SerializeToNode(written, ActionReaderJson);
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET revision_json = '{broken'");
        var reviews = ActionReviews();

        var found = await reviews.GetRevisionAsync(RecordId, 1, default);
        var review = await reviews.GetAsync(RecordId, default);
        var evidence = await ActionStore().ReadEvidenceAsync(written.CandidateId, false, default);

        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(found, ActionReaderJson)));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(review!.Review.CurrentRevision, ActionReaderJson)));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(evidence!.Current, ActionReaderJson)));
    }

    [Fact]
    public async Task EveryReaderFailsOnARowWithNullColumnsAndNeverFallsBackToTheJsonBecauseASilentFallbackWouldHideAnUnmovedRow()
    {
        var written = await SettleFullRevisionAsync();
        await MakeActionRevisionsLegacyAsync(1);
        var reviews = ActionReviews();
        var latest = Enum.Parse(typeof(PostgresReviewQueueStore).Assembly.GetType("CivicLens.Infrastructure.Relationships.RelationshipRevisionScope")!, "Latest");
        var officialIds = ((string)(await ScalarAsync("SELECT array_to_string(official_ids, ',') FROM action_drafts"))!).Split(',');
        await using var db = await new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options).CreateDbContextAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetRevisionAsync(RecordId, 1, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetAsync(RecordId, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => ActionStore().ReadEvidenceAsync(written.CandidateId, false, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetForPublicationAsync([RecordId], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.ListUnpublishedApprovedAsync(10, [], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PostgresReviewQueueStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options)).SearchApprovedAsync("Warren", null, 10, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Relationships.RelationshipEvidenceReader",
            "ReadActionAsync", db, RecordId, latest, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Relationships.RelationshipEvidenceReader",
            "ReadActionsSponsoredByAsync", db, officialIds, latest, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Actions.ActionRevisionRows",
            "ReadNamedAsync", db, new[] { new PublishedRevisionBinding(PublishedRecordKind.Action, RecordId, 1) },
            CancellationToken.None));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions WHERE headline IS NULL"));
    }

    [Fact]
    public async Task TheNamedReaderFailsWhenARevisionIsNotStoredBecauseAFabricatedCitationMustNeverBeSkipped()
    {
        _ = await SettleFullRevisionAsync();
        await using var db = await new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options).CreateDbContextAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Actions.ActionRevisionRows",
            "ReadNamedAsync", db, new[] { new PublishedRevisionBinding(PublishedRecordKind.Action, RecordId, 9) },
            CancellationToken.None));
    }

    [Fact]
    public async Task TheQueriesOfAnActionHistoryReadStayTheSameWhenMoreActionsAreReadBecauseNoReaderQueriesEachRow()
    {
        var counter = new ActionReaderQueryCounter();
        _ = await SettleFullRevisionAsync();
        var officialIds = ((string)(await ScalarAsync("SELECT array_to_string(official_ids, ',') FROM action_drafts"))!).Split(',');
        var latest = Enum.Parse(typeof(PostgresReviewQueueStore).Assembly.GetType("CivicLens.Infrastructure.Relationships.RelationshipRevisionScope")!, "Latest");
        await using var db = await new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options).CreateDbContextAsync();
        async Task<int> ReadAsync()
        {
            dynamic read = InternalMethods.InvokeAsync("CivicLens.Infrastructure.Relationships.RelationshipEvidenceReader",
                "ReadActionsSponsoredByAsync", db, officialIds, latest, CancellationToken.None);
            return (await read).Length;
        }
        Assert.Equal(1, await ReadAsync());
        counter.Count = 0;
        await ReadAsync();
        var queriesForOne = counter.Count;
        foreach (var (digit, index) in new[] { 'a', 'b', 'c' }.Select((digit, index) => (digit, index)))
            await CopyActionAsync(new string(digit, 64), 8 + index);
        counter.Count = 0;

        Assert.Equal(4, await ReadAsync());
        Assert.Equal(queriesForOne, counter.Count);
    }

    private Task CopyActionAsync(string recordId, int billNumber)
    {
        string Copy(string table, string overrides) => $"""
            INSERT INTO {table} SELECT (jsonb_populate_record(NULL::{table}, to_jsonb(t) || {overrides})).* FROM {table} t WHERE t.record_id = '{RecordId}';
            """;
        var id = $"jsonb_build_object('record_id', '{recordId}')";
        return ExecuteAsync(connectionString, string.Concat(
            Copy("action_drafts", $"jsonb_build_object('record_id', '{recordId}', 'bill_number', {billNumber})"),
            Copy("action_revisions", $"jsonb_build_object('record_id', '{recordId}', 'bill_number', {billNumber}, 'run_id', NULL, 'author_kind', 'Person', 'text_origin', 'Person')"),
            Copy("action_revision_sponsorships", id),
            Copy("action_revision_sponsorship_citations", id),
            Copy("action_revision_issues", id)));
    }

    private sealed class ActionReaderQueryCounter : DbCommandInterceptor
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
