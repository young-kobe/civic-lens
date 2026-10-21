namespace CivicLens.Application.Reports;

public sealed class PullReaderReports(IReaderReportSource source, IReaderReportStore store)
{
    /// <summary>Pulls pages until the service has no more. Each page is checked, then stored with its cursor.</summary>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
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
}
