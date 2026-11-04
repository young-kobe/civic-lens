using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
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
        var evidence = await ActionStore().ReadEvidenceAsync(written.CandidateId, default);
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
        var evidence = await ActionStore().ReadEvidenceAsync(written.CandidateId, default);

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
        await using var db = await new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options).CreateDbContextAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetRevisionAsync(RecordId, 1, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetAsync(RecordId, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => ActionStore().ReadEvidenceAsync(written.CandidateId, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetForPublicationAsync([RecordId], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.ListUnpublishedApprovedAsync(10, [], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PostgresReviewQueueStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options)).SearchApprovedAsync("Warren", null, 10, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => InternalMethods.InvokeAsync("CivicLens.Infrastructure.Relationships.RelationshipEvidenceReader",
            "ReadActionAsync", db, RecordId, latest, CancellationToken.None));
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
}
