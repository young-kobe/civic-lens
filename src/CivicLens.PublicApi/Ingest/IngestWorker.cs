using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CivicLens.PublicApi.Ingest;

/// <summary>Runs the ingest on a fixed interval. A failed run waits for the next interval, and the next run starts from the same state.</summary>
public sealed class IngestWorker(ReleaseIngest ingest, PublicApiOptions options, ILogger<IngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        do
        {
            await RunOnceAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await ingest.RunAsync(cancellationToken);
            if (result.Outcome == IngestOutcome.Ingested)
                logger.LogInformation("Ingested release {Release}: {Indexed} records indexed, {Closed} closed.",
                    result.ReleaseNumber, result.Indexed, result.Closed);
            foreach (var failure in result.Failures)
                logger.LogError("Record {RecordId} is not indexed: {Reason}", failure.RecordId, failure.Reason);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The release ingest failed. It runs again at the next interval.");
        }
    }
}
