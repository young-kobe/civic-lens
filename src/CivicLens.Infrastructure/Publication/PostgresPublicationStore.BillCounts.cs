using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

public sealed partial class PostgresPublicationStore
{
    public async Task<BillCountReleasePage> ReadBillCountReleasePageAsync(int afterReleaseNumber, string? selectionFingerprint, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var selection = await ReadFingerprintedSelectionAsync(db, readServedDirectory, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (selectionFingerprint is not null && selectionFingerprint != selection.Fingerprint)
            return new([], selection.Fingerprint, false, true);
        var following = selection.Releases.Where(release => release.ReleaseNumber > afterReleaseNumber).Take(limit + 1).ToArray();
        return new([.. following.Take(limit)], selection.Fingerprint, following.Length > limit);
    }

    public async Task<ReleaseBillCountSource> ReadBillCountSourceAsync(int releaseNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var source = await ReleaseBillCountSources.ReadAsync(db, releaseNumber, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return source;
    }

    public async Task<bool> CommitBillCountsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ReviewKeysInOrder, cancellationToken);
        var current = await RequireRetainedReleaseAsync(db, release, readServedDirectory, cancellationToken);
        await ReleaseBillCountSources.RequireSameMembersAsync(db, release.ReleaseNumber, validatedMembers, cancellationToken);
        var added = await ReleaseBillCounts.FillAsync(db, release.ReleaseNumber, CongressOfRelease(current), validatedMembers, cancellationToken);
        if (!await db.Set<ReleaseBillCountRow>().AnyAsync(row => row.ReleaseNumber == release.ReleaseNumber, cancellationToken))
            throw new BillCountRefusedException($"Release {release.ReleaseNumber} has no Congress to count bills for.");
        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    public async Task<BillCountStatus> VerifyBillCountsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var current = await RequireRetainedReleaseAsync(db, release, readServedDirectory, cancellationToken);
        await ReleaseBillCountSources.RequireSameMembersAsync(db, release.ReleaseNumber, validatedMembers, cancellationToken);
        var status = await ReleaseBillCounts.VerifyAsync(db, release.ReleaseNumber, CongressOfRelease(current), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return status;
    }

    public async Task<bool> CommitPartyLineUpsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ReviewKeysInOrder, cancellationToken);
        var current = await RequireRetainedReleaseAsync(db, release, readServedDirectory, cancellationToken);
        await ReleaseBillCountSources.RequireSameMembersAsync(db, release.ReleaseNumber, validatedMembers, cancellationToken);
        var added = await ReleaseIssuePartyCounts.FillAsync(db, release.ReleaseNumber, CongressOfRelease(current), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    public async Task<BillCountStatus> VerifyPartyLineUpsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readServedDirectory);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var current = await RequireRetainedReleaseAsync(db, release, readServedDirectory, cancellationToken);
        await ReleaseBillCountSources.RequireSameMembersAsync(db, release.ReleaseNumber, validatedMembers, cancellationToken);
        var status = await ReleaseIssuePartyCounts.VerifyAsync(db, release.ReleaseNumber, CongressOfRelease(current), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return status;
    }

    private static int? CongressOfRelease(PublicationReleaseRow release) =>
        CongressCalendar.CongressOn(new DatabaseTime(release.PublishedAtUtcTicks).Day);
}
