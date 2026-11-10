using CivicLens.Application.Publication;

namespace CivicLens.Infrastructure.Publication;

public sealed partial class PostgresPublicationStore
{
    public async Task<IReadOnlyDictionary<PublishedRecordReference, StoredCorrectionHistory>> ReadCorrectionHistoriesAsync(
        int releaseNumber, IReadOnlyCollection<PublishedRevisionBinding> records, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await CorrectionHistoryCopies.ReadAsync(db, releaseNumber, records, cancellationToken);
    }

    public async Task<IReadOnlyList<CorrectionHistoryBinding>> PrepareCorrectionHistoriesAsync(
        IReadOnlyCollection<PublicationCorrectionHistory> histories, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await CorrectionHistoryCopies.PrepareAsync(db, histories, cancellationToken);
    }
}
