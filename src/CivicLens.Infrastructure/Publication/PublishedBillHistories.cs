using System.Data;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.Infrastructure.Publication;

internal static class PublishedBillHistories
{
    public static async Task EnsureAsync(CollectionAttemptDbContext db, BillHistoryCapture capture, CancellationToken cancellationToken)
    {
        // The snapshot keeps the capture fixed while the body is copied, without holding review locks.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var existing = await db.Set<PublishedBillHistoryRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.DetailJobId == capture.DetailJobId &&
                row.ProjectionVersion == capture.ProjectionVersion, cancellationToken);
        if (existing is not null)
        {
            RequireMatchingMetadata(existing, capture);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (await BillHistoryCaptures.ReadCompleteAsync(db, capture.DetailJobId, cancellationToken) != capture)
            throw new PublicationConflictException("The selected bill history is no longer complete or has different capture metadata.");
        var publicLaw = await PublicLawAsync(db, capture.DetailJobId, cancellationToken);
        // INSERT SELECT streams the body inside Postgres; no action object graph is loaded by the publisher.
        var copied = await db.Database.ExecuteSqlRawAsync("""
            WITH inserted AS (
                INSERT INTO published_bill_histories
                    (detail_job_id, projection_version, source_id, congress, bill_type, bill_number, observed_at_utc_ticks, action_count, public_law)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})
                ON CONFLICT DO NOTHING RETURNING detail_job_id, projection_version)
            INSERT INTO published_bill_history_actions
                (detail_job_id, projection_version, position, action_date, action_time, text, type, action_code, source_system_code, source_system_name)
            SELECT a.detail_job_id, i.projection_version, a.position, a.action_date, a.action_time, a.text, a.type, a.action_code, a.source_system_code, a.source_system_name
              FROM bill_actions a JOIN inserted i ON i.detail_job_id = a.detail_job_id
            """, [capture.DetailJobId, capture.ProjectionVersion, capture.SourceId, capture.Congress, capture.BillType, capture.BillNumber,
                capture.ObservedAtUtc.UtcTicks, capture.ActionCount, new NpgsqlParameter("public_law", NpgsqlDbType.Text) { Value = (object?)publicLaw ?? DBNull.Value }], cancellationToken);
        if (copied != capture.ActionCount)
            throw new InvalidDataException("The copied history body does not match its verified import count.");
        await transaction.CommitAsync(cancellationToken);
    }

    public static void RequireMatchingMetadata(PublishedBillHistoryRow row, BillHistoryCapture capture)
    {
        if (row.DetailJobId != capture.DetailJobId || row.ProjectionVersion != capture.ProjectionVersion ||
            row.SourceId != capture.SourceId || row.Congress != capture.Congress || row.BillType != capture.BillType ||
            row.BillNumber != capture.BillNumber || row.ObservedAtUtcTicks != capture.ObservedAtUtc.UtcTicks || row.ActionCount != capture.ActionCount)
            throw new InvalidDataException("A history capture was reused with different metadata.");
    }

    private static async Task<string?> PublicLawAsync(CollectionAttemptDbContext db, string detailJobId, CancellationToken cancellationToken)
    {
        var candidates = db.Set<BillActionRow>().AsNoTracking()
            .Where(row => row.DetailJobId == detailJobId && row.Text.StartsWith("Became Public Law No:"))
            .OrderByDescending(row => row.Position).Select(row => row.Text).AsAsyncEnumerable();
        await foreach (var text in candidates.WithCancellation(cancellationToken))
            if (BillStatusMeaning.PublicLawNumberOf(text) is { } number) return number;
        return null;
    }
}
