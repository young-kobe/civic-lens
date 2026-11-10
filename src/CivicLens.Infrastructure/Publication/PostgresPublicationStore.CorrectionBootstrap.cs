using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

public sealed partial class PostgresPublicationStore
{
    public async Task<CorrectionHistoryBootstrapPage> ReadCorrectionHistoryBootstrapPageAsync(CorrectionHistoryBootstrapCursor? after,
        int limit, Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var plan = await PublicationReleaseRetention.ReadAsync(db, cancellationToken);
        var servedDirectory = await readServedDirectory(cancellationToken);
        var selected = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .Where(row => plan.KeptNumbers.Contains(row.ReleaseNumber) || row.IsActive || row.DirectoryName == servedDirectory)
            .Select(row => row.ReleaseNumber).ToArrayAsync(cancellationToken);
        return await PublishedEntries.ReadCorrectionBootstrapPageAsync(db, after, selected,
            limit, cancellationToken);
    }

    public async Task<(int Added, int AlreadyPresent)> CommitCorrectionHistoryBootstrapAsync(PublicationReleaseSummary release,
        IReadOnlyCollection<CorrectionHistoryBinding> bindings, Func<CancellationToken, Task<string?>> readServedDirectory,
        CancellationToken cancellationToken)
    {
        if (bindings.Count == 0) return (0, 0);
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ReviewKeysInOrder, cancellationToken);
        var plan = await PublicationReleaseRetention.ReadAsync(db, cancellationToken);
        var current = await db.Set<PublicationReleaseRow>().SingleOrDefaultAsync(row => row.ReleaseNumber == release.ReleaseNumber,
            cancellationToken) ?? throw new InvalidDataException("The prepared release no longer exists.");
        var active = current.IsActive;
        var served = await readServedDirectory(cancellationToken);
        var retained = plan.KeptNumbers.Contains(release.ReleaseNumber) || active || current.DirectoryName == served;
        if (!retained || current.DirectoryName != release.DirectoryName)
            throw new InvalidDataException("The prepared release is no longer retained.");
        var names = bindings.Select(binding => (PublishedRecordReference.NameOf(binding.Record.Kind), binding.Record.RecordId,
            binding.Record.RevisionNumber)).ToArray();
        var actual = await db.Database.SqlQueryRaw<BootstrapMembershipRow>("""
            SELECT k.kind AS "Kind", k.record_id AS "RecordId", k.revision_number AS "RevisionNumber"
              FROM unnest({0}::text[], {1}::text[], {2}::int[]) AS k(kind, record_id, revision_number)
              JOIN publication_record_states s ON s.kind = k.kind AND s.record_id = k.record_id AND s.revision_number = k.revision_number
             WHERE s.first_release <= {3} AND (s.last_release IS NULL OR s.last_release >= {3})
            """, names.Select(key => key.Item1).ToArray(), names.Select(key => key.RecordId).ToArray(),
            names.Select(key => key.RevisionNumber).ToArray(), release.ReleaseNumber).ToArrayAsync(cancellationToken);
        if (actual.Length != names.Length) throw new InvalidDataException("The prepared records no longer match the release.");
        var result = await CorrectionHistoryCopies.BindBootstrapAsync(db, release.ReleaseNumber, bindings, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private sealed class BootstrapMembershipRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
