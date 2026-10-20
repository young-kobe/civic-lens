using CivicLens.PublicApi.Reports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Runs the ingest and the report purge on a fixed interval. A failed step waits for the next interval, and the next run starts from the same state.
/// </summary>
public sealed class IngestWorker(ReleaseIngest ingest, ReportStore reports, PublicApiOptions options, ILogger<IngestWorker> logger) : BackgroundService
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
        await GuardAsync(IngestAsync, "The release ingest failed. It runs again at the next interval.", cancellationToken);
        await GuardAsync(PurgeAsync, "The report purge failed. It runs again at the next interval.", cancellationToken);
    }

    private async Task IngestAsync(CancellationToken cancellationToken)
    {
        var result = await ingest.RunAsync(cancellationToken);
        if (result.Outcome == IngestOutcome.Ingested)
            logger.LogInformation("Ingested release {Release}: {Indexed} records indexed, {Closed} closed.",
                result.ReleaseNumber, result.Indexed, result.Closed);
        foreach (var failure in result.Failures)
            logger.LogError("Record {RecordId} is not indexed: {Reason}", failure.RecordId, failure.Reason);
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var purged = await reports.PurgeAsync(cancellationToken);
        if (purged > 0) logger.LogInformation("Purged {Purged} pulled reports.", purged);
    }

    private async Task GuardAsync(Func<CancellationToken, Task> step, string failure, CancellationToken cancellationToken)
    {
        try
        {
            await step(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "{Failure}", failure);
        }
    }
}
