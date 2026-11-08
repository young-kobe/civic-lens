using System.Collections.Immutable;
using CivicLens.Application.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Documents;

public sealed class BackfillDocumentComparisonDetailsTests
{
    [Fact]
    public async Task RequiresOwnerAndPositiveLimitBeforeReadingStore()
    {
        var store = new RecordingStore();
        var handler = new BackfillDocumentComparisonDetails(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(
            new ReviewActor("reviewer", ReviewRole.Reviewer), 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(
            new ReviewActor("owner", ReviewRole.Owner), 0));

        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task CountsSkippedRowsAcrossBatchesAndReportsAtMostFiftyIds()
    {
        var batches = new[]
        {
            SkippedBatch("a", 25),
            SkippedBatch("b", 25),
            SkippedBatch("c", 7)
        };
        var store = new RecordingStore(batches);

        var result = await new BackfillDocumentComparisonDetails(store)
            .ExecuteAsync(new ReviewActor("owner", ReviewRole.Owner), 57);

        Assert.Equal(57, result.Examined);
        Assert.Equal(0, result.Moved);
        Assert.Equal(57, result.Skipped);
        Assert.Equal(Enumerable.Range(0, 50).Select(index => $"id-{index:D2}"), result.SkippedComparisonIds);
        Assert.Equal(
            new (string? AfterId, int Size)[] { (null, 25), ("last-25", 25), ("last-50", 7) },
            store.Requests);
        Assert.Equal(3, store.Calls);
    }

    private static DocumentComparisonBackfillBatch SkippedBatch(string batch, int count)
    {
        var firstIndex = batch switch { "a" => 0, "b" => 25, _ => 50 };
        return new DocumentComparisonBackfillBatch(count,
            Enumerable.Range(firstIndex, count).Select(index => $"id-{index:D2}").ToImmutableArray(),
            $"last-{firstIndex + count}");
    }

    private sealed class RecordingStore(DocumentComparisonBackfillBatch[]? batches = null) : IDocumentComparisonBackfillStore
    {
        public int Calls { get; private set; }
        public List<(string? AfterId, int Size)> Requests { get; } = [];

        public Task<DocumentComparisonBackfillBatch> BackfillBatchAsync(string? afterComparisonId, int size,
            CancellationToken cancellationToken)
        {
            Requests.Add((afterComparisonId, size));
            var batch = batches is null ? new DocumentComparisonBackfillBatch(0, [], null) : batches[Calls];
            Calls++;
            return Task.FromResult(batch);
        }
    }
}
