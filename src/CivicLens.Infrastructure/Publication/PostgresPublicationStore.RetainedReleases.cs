using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Owns the selection of retained, active, and served releases that the owner backfills and verifiers share.</summary>
public sealed partial class PostgresPublicationStore
{
    public async Task<string> ReadRetainedReleaseFingerprintAsync(
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var selection = await ReadFingerprintedSelectionAsync(db, readServedDirectory, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return selection.Fingerprint;
    }

    public async Task<IReadOnlyList<PublicationReleaseSummary>> ReadRetainedReleaseSelectionAsync(
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var selected = await ReadRetainedReleasesAsync(db, readServedDirectory, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return selected;
    }

    private static async Task<IDbContextTransaction> BeginVerificationReadAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        return transaction;
    }

    private static async Task<RetainedReleaseSelection> ReadFingerprintedSelectionAsync(CollectionAttemptDbContext db,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        var selected = await ReadRetainedReleasesAsync(db, readServedDirectory, cancellationToken);
        var fields = selected.Select(row => string.Concat(row.ReleaseNumber.ToString(CultureInfo.InvariantCulture), ":",
            row.DirectoryName.Length.ToString(CultureInfo.InvariantCulture), ":", row.DirectoryName));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", fields)))).ToLowerInvariant();
        return new(selected, fingerprint);
    }

    private static async Task<PublicationReleaseSummary[]> ReadRetainedReleasesAsync(CollectionAttemptDbContext db,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        var plan = await PublicationReleaseRetention.ReadAsync(db, cancellationToken);
        var servedDirectory = await readServedDirectory(cancellationToken);
        var rows = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .Where(row => plan.KeptNumbers.Contains(row.ReleaseNumber) || row.IsActive || row.DirectoryName == servedDirectory)
            .OrderBy(row => row.ReleaseNumber).ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToSummary)];
    }

    private static async Task<PublicationReleaseRow> RequireRetainedReleaseAsync(CollectionAttemptDbContext db, PublicationReleaseSummary release,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        var plan = await PublicationReleaseRetention.ReadAsync(db, cancellationToken);
        var current = await db.Set<PublicationReleaseRow>().SingleOrDefaultAsync(row => row.ReleaseNumber == release.ReleaseNumber,
            cancellationToken) ?? throw new InvalidDataException("The prepared release no longer exists.");
        var served = await readServedDirectory(cancellationToken);
        var retained = plan.KeptNumbers.Contains(release.ReleaseNumber) || current.IsActive || current.DirectoryName == served;
        if (!retained || current.DirectoryName != release.DirectoryName)
            throw new InvalidDataException("The prepared release is no longer retained.");
        return current;
    }

    private sealed record RetainedReleaseSelection(PublicationReleaseSummary[] Releases, string Fingerprint)
    {
    }
}
