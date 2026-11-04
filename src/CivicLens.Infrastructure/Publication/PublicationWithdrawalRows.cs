using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublicationWithdrawalRows
{
    private const string CloseRanges = """
        UPDATE publication_withdrawals w SET last_release = {0}
        FROM unnest({1}::text[], {2}::text[]) AS k(kind, record_id)
        WHERE w.kind = k.kind AND w.record_id = k.record_id AND w.last_release IS NULL
        """;

    public static async Task RecordAsync(CollectionAttemptDbContext db, PublicationCommit commit, CancellationToken cancellationToken)
    {
        var before = await db.Set<PublicationWithdrawalRow>().AsNoTracking()
            .Where(row => row.LastRelease == null)
            .ToDictionaryAsync(row => (row.Kind, row.RecordId), row => row.WithdrawnAtUtcTicks, cancellationToken);
        var after = commit.Withdrawals.ToDictionary(withdrawal => (Kind: PublishedRecordReference.NameOf(withdrawal.Kind), withdrawal.RecordId));

        var closed = before.Where(entry => !after.TryGetValue(entry.Key, out var withdrawal) || withdrawal.WithdrawnAtUtc.UtcTicks != entry.Value)
            .Select(entry => entry.Key).ToArray();
        var opened = after.Where(entry => !before.TryGetValue(entry.Key, out var ticks) || ticks != entry.Value.WithdrawnAtUtc.UtcTicks)
            .Select(entry => entry.Value).ToArray();
        if (closed.Length > 0)
            await db.Database.ExecuteSqlRawAsync(CloseRanges,
                [commit.ReleaseNumber - 1, closed.Select(key => key.Kind).ToArray(), closed.Select(key => key.RecordId).ToArray()], cancellationToken);
        if (opened.Length > 0) await OpenAsync(db, commit.ReleaseNumber, opened, cancellationToken);
        await RequireOneRowForEachWithdrawalAsync(db, commit, cancellationToken);
    }

    public static async Task<IReadOnlyList<PublishedWithdrawal>> ReadAsync(CollectionAttemptDbContext db, int releaseNumber,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<PublicationWithdrawalRow>().AsNoTracking()
            .Where(row => row.FirstRelease <= releaseNumber && (row.LastRelease == null || row.LastRelease >= releaseNumber))
            .ToArrayAsync(cancellationToken);
        var corrections = await (from correction in db.Set<PublicationWithdrawalCorrectionRow>().AsNoTracking()
                                 join row in db.Set<PublicationWithdrawalRow>() on new { correction.Kind, correction.RecordId, correction.FirstRelease }
                                     equals new { row.Kind, row.RecordId, row.FirstRelease }
                                 where row.FirstRelease <= releaseNumber && (row.LastRelease == null || row.LastRelease >= releaseNumber)
                                 orderby correction.Position
                                 select correction).ToArrayAsync(cancellationToken);
        var kinds = rows.ToDictionary(row => row.Kind, row => Enum.Parse<PublishedRecordKind>(row.Kind, ignoreCase: true));
        var firstPublished = await PublishedRecords.ReadAsync(db,
            [.. rows.Select(row => new PublishedRecordReference(kinds[row.Kind], row.RecordId))], cancellationToken);
        return [.. rows.Select(row => new PublishedWithdrawal
        {
            Kind = kinds[row.Kind],
            RecordId = row.RecordId,
            WithdrawnRevisionNumber = row.WithdrawnRevisionNumber,
            FirstPublishedAtUtc = firstPublished[new(kinds[row.Kind], row.RecordId)],
            WithdrawnAtUtc = new DateTimeOffset(row.WithdrawnAtUtcTicks, TimeSpan.Zero),
            Note = row.Note,
            Corrections = [.. corrections.Where(correction => correction.Kind == row.Kind && correction.RecordId == row.RecordId &&
                    correction.FirstRelease == row.FirstRelease)
                .Select(correction => new PublishedCorrection
                {
                    ReplacedRevisionNumber = correction.ReplacedRevisionNumber,
                    PublishedAtUtc = new DateTimeOffset(correction.PublishedAtUtcTicks, TimeSpan.Zero),
                    Note = correction.Note,
                    FactsUnchanged = correction.FactsUnchanged
                })]
        })];
    }

    private static async Task OpenAsync(CollectionAttemptDbContext db, int release, PublishedWithdrawal[] withdrawals,
        CancellationToken cancellationToken)
    {
        db.AddRange(withdrawals.Select(withdrawal => new PublicationWithdrawalRow
        {
            Kind = PublishedRecordReference.NameOf(withdrawal.Kind),
            RecordId = withdrawal.RecordId,
            WithdrawnRevisionNumber = withdrawal.WithdrawnRevisionNumber,
            WithdrawnAtUtcTicks = withdrawal.WithdrawnAtUtc.UtcTicks,
            Note = withdrawal.Note,
            FirstRelease = release
        }));
        db.AddRange(withdrawals.SelectMany(withdrawal => withdrawal.Corrections.Select((correction, position) => new PublicationWithdrawalCorrectionRow
        {
            Kind = PublishedRecordReference.NameOf(withdrawal.Kind),
            RecordId = withdrawal.RecordId,
            FirstRelease = release,
            Position = position,
            ReplacedRevisionNumber = correction.ReplacedRevisionNumber,
            PublishedAtUtcTicks = correction.PublishedAtUtc.UtcTicks,
            Note = correction.Note,
            FactsUnchanged = correction.FactsUnchanged
        })));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RequireOneRowForEachWithdrawalAsync(CollectionAttemptDbContext db, PublicationCommit commit,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<PublicationWithdrawalRow>().AsNoTracking()
            .CountAsync(row => row.FirstRelease <= commit.ReleaseNumber && (row.LastRelease == null || row.LastRelease >= commit.ReleaseNumber),
                cancellationToken);
        if (rows != commit.Withdrawals.Length)
            throw new PublicationStateMismatchException(
                $"Release {commit.ReleaseNumber} lists {commit.Withdrawals.Length} withdrawals, but the withdrawal table holds {rows} rows for it.");
    }
}
