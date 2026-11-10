using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        var selected = await ReadRetainedCorrectionReleasesAsync(db, readServedDirectory, cancellationToken);
        return await PublishedEntries.ReadCorrectionBootstrapPageAsync(db, after, selected.Select(row => row.ReleaseNumber).ToArray(),
            limit, cancellationToken);
    }

    public async Task<CorrectionHistoryVerificationPage> ReadCorrectionHistoryVerificationPageAsync(CorrectionHistoryVerificationCursor? after,
        int limit, Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginCorrectionVerificationReadAsync(db, cancellationToken);
        var selection = await ReadCorrectionVerificationSelectionAsync(db, readServedDirectory, cancellationToken);
        var fingerprint = selection.Fingerprint;
        if (after is not null && after.SelectionFingerprint != fingerprint)
        {
            await transaction.CommitAsync(cancellationToken);
            return new([], fingerprint, null, true);
        }
        var page = await PublishedEntries.ReadCorrectionVerificationPageAsync(db, after, fingerprint,
            selection.ReleaseNumbers, limit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    public async Task<string> ReadCorrectionHistoryVerificationFingerprintAsync(
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginCorrectionVerificationReadAsync(db, cancellationToken);
        var selection = await ReadCorrectionVerificationSelectionAsync(db, readServedDirectory, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return selection.Fingerprint;
    }

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginCorrectionVerificationReadAsync(
        CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        return transaction;
    }

    private static async Task<CorrectionVerificationSelection> ReadCorrectionVerificationSelectionAsync(CollectionAttemptDbContext db,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        var selected = await ReadRetainedCorrectionReleasesAsync(db, readServedDirectory, cancellationToken);
        var fields = selected.Select(row => string.Concat(row.ReleaseNumber.ToString(CultureInfo.InvariantCulture), ":",
            row.DirectoryName.Length.ToString(CultureInfo.InvariantCulture), ":", row.DirectoryName));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", fields)))).ToLowerInvariant();
        return new([.. selected.Select(row => row.ReleaseNumber)], fingerprint);
    }

    private static async Task<(int ReleaseNumber, string DirectoryName)[]> ReadRetainedCorrectionReleasesAsync(CollectionAttemptDbContext db,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        var plan = await PublicationReleaseRetention.ReadAsync(db, cancellationToken);
        var servedDirectory = await readServedDirectory(cancellationToken);
        var rows = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .Where(row => plan.KeptNumbers.Contains(row.ReleaseNumber) || row.IsActive || row.DirectoryName == servedDirectory)
            .OrderBy(row => row.ReleaseNumber).Select(row => new { row.ReleaseNumber, row.DirectoryName }).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => (row.ReleaseNumber, row.DirectoryName))];
    }

    private sealed record CorrectionVerificationSelection(int[] ReleaseNumbers, string Fingerprint);

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
