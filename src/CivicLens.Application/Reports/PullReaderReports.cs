using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Reports;

public sealed class PullReaderReports(IReaderReportSource source, IReaderReportStore store, ILogger<PullReaderReports> logger)
{
    /// <summary>Deletes the closed reports past their retention, then pulls pages until the service has no more. Each page is checked, then stored with its cursor.</summary>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        await DeleteExpiredClosedAsync(cancellationToken);
        var pulled = 0;
        var cursor = await store.GetCursorAsync(cancellationToken);
        while (true)
        {
            var batch = await source.PullAsync(cursor, cancellationToken);
            batch.Validate();
            await store.SavePulledAsync(batch, cancellationToken);
            pulled += batch.Reports.Length;
            if (batch.Reports.Length == 0 || batch.NextCursor == cursor) return pulled;
            cursor = batch.NextCursor;
        }
    }

    private async Task DeleteExpiredClosedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await store.DeleteExpiredClosedAsync(cancellationToken);
            if (deleted > 0) logger.LogInformation("Deleted {Count} closed reader reports past their retention.", deleted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
        {
            logger.LogError(exception, "The deletion of expired closed reader reports failed. The pull goes on.");
        }
    }
}
