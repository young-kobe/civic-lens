using CivicLens.PublicApi;
using CivicLens.PublicApi.Ingest;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.PublicApi.Ingest;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class IngestWorkerTests(PostgresCollection postgres)
{
    [Fact]
    public async Task AFeedTimeoutIsLoggedAndTheNextTickRunsSoOneSlowReplyNeverStopsTheService()
    {
        await using var world = await SearchWorld.CreateAsync(postgres);
        world.Site.Publish(1, PublishedSamples.Statement('a', "Rent cap"));
        world.Site.FailNextManifestRequest = new TaskCanceledException("The request timed out.");
        var logger = new ListLogger();
        using var worker = new IngestWorker(world.Ingest, world.Reports, world.Options with { PollInterval = TimeSpan.FromMilliseconds(50) }, logger);

        await worker.StartAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 100 && await world.Index.FindReleaseAsync(null, CancellationToken.None) is null; attempt++)
            await Task.Delay(100);
        var stored = await world.Index.FindReleaseAsync(null, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, stored?.ReleaseNumber);
        Assert.Contains(logger.Errors, error => error is TaskCanceledException);
        Assert.False(worker.ExecuteTask!.IsFaulted);
    }

    private sealed class ListLogger : ILogger<IngestWorker>
    {
        public List<Exception?> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) lock (Errors) Errors.Add(exception);
        }
    }
}
