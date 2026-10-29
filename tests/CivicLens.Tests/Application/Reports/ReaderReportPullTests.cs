using CivicLens.Application;
using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Publication.Contracts.PulledReportTests;

namespace CivicLens.Tests.Application.Reports;

public sealed class ReaderReportPullTests
{
    private readonly FakeReaderReportStore store = new();

    [Fact]
    public async Task AllPagesAreStoredAndEachPullAsksWithTheCursorThatTheStoreSavedNeverOneThatTheApplicationMadeUp()
    {
        var source = new ScriptedSource(Batch(Report(), Report()) with { NextCursor = "c1" }, Batch(Report()) with { NextCursor = "c2" },
            Batch() with { NextCursor = "c2" });

        var pulled = await Pull(source).ExecuteAsync(default);

        Assert.Equal(3, pulled);
        Assert.Equal(new string?[] { null, "c1", "c2" }, source.CursorsAsked);
        Assert.Equal("c2", store.Cursor);
        Assert.Equal(3, store.Stored.Count);
    }

    [Fact]
    public async Task ABatchWithOneBadReportIsRefusedAsAWholeAndTheCursorStaysSoTheNextTickAsksForTheSameReports()
    {
        var first = Report();
        var source = new ScriptedSource(Batch(first) with { NextCursor = "c1" },
            Batch(Report(), Report() with { RevisionNumber = 0 }) with { NextCursor = "c2" });

        await Assert.ThrowsAsync<InvalidDataException>(() => Pull(source).ExecuteAsync(default));

        Assert.Equal("c1", store.Cursor);
        Assert.Equal([first.ReportId], store.Stored);
    }

    [Fact]
    public async Task AServiceThatRepeatsItsCursorWithReportsDoesNotHoldThePullInALoop()
    {
        var source = new ScriptedSource(Batch(Report()) with { NextCursor = "c1" }, Batch(Report()) with { NextCursor = "c1" });

        var pulled = await Pull(source).ExecuteAsync(default);

        Assert.Equal((2, 2), (pulled, store.Stored.Count));
    }

    [Fact]
    public async Task WhenTheServiceIsDownTheStageCountsAFailureKeepsTheCursorAndPullsAgainOnTheNextTick()
    {
        var source = new ScriptedSource(Batch(Report()) with { NextCursor = "c1" }, Batch() with { NextCursor = "c1" });
        var pull = Pull(source);
        await pull.ExecuteAsync(default);
        var logger = new CapturingLogger<StageWorker>();
        var worker = new StageWorker("reportPull", pull.ExecuteAsync, new CountingWakeup(), logger, TestBeat.Create());

        source.Failure = new HttpRequestException("connection refused");
        var down = await worker.ExecuteAsync(once: true);
        source.Failure = null;
        source.Enqueue(Batch(Report()) with { NextCursor = "c2" }, Batch() with { NextCursor = "c2" });
        var recovered = await worker.ExecuteAsync(once: true);

        Assert.Equal((1, 0), (down.Failures, down.ProgressCount));
        Assert.Equal("connection refused", Assert.Single(logger.Entries).Exception!.Message);
        Assert.Equal((0, 1), (recovered.Failures, recovered.ProgressCount));
        Assert.Equal("c2", store.Cursor);
    }

    [Fact]
    public async Task AFailedDeletionOfExpiredReportsIsLoggedAndTheNewReportsAreStillPulledSoAStuckDeleteNeverBlocksTheQueue()
    {
        var logger = new CapturingLogger<PullReaderReports>();
        store.DeleteFailure = new InvalidOperationException("database busy");
        var source = new ScriptedSource(Batch(Report()) with { NextCursor = "c1" }, Batch() with { NextCursor = "c1" });

        var pulled = await new PullReaderReports(source, store, logger).ExecuteAsync(default);

        Assert.Equal((1, 1, 1), (pulled, store.Stored.Count, store.DeleteCalls));
        Assert.Equal("database busy", Assert.Single(logger.Entries).Exception!.Message);
    }

    private PullReaderReports Pull(IReaderReportSource source) => new(source, store, new CapturingLogger<PullReaderReports>());

    private sealed class ScriptedSource(params PulledReportBatch[] batches) : IReaderReportSource
    {
        private readonly Queue<PulledReportBatch> queue = new(batches);

        public List<string?> CursorsAsked { get; } = [];
        public Exception? Failure { get; set; }

        public void Enqueue(params PulledReportBatch[] more)
        {
            foreach (var batch in more) queue.Enqueue(batch);
        }

        public Task<PulledReportBatch> PullAsync(string? cursor, CancellationToken cancellationToken)
        {
            CursorsAsked.Add(cursor);
            return Failure is { } failure ? Task.FromException<PulledReportBatch>(failure) : Task.FromResult(queue.Dequeue());
        }
    }
}
