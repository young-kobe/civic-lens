using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CivicLens.Infrastructure;

internal static class PostgresAdvisoryLock
{
    // A transaction that holds several keys takes them in this declaration order.
    internal const string CollectionJobs = "civic-lens-collection-jobs";
    internal const string DocumentChangeReview = "civic-lens-document-change-review";
    internal const string StatementReview = "civic-lens-statement-review";
    internal const string ActionReview = "civic-lens-action-review";
    // No transaction holds these with another key.
    internal const string CollectionImport = "civic-lens-collection-import";
    internal const string DocumentExtractions = "civic-lens-document-extractions";
    internal const string DocumentComparisons = "civic-lens-document-comparisons";

    private const string AcquireSql = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";

    internal static readonly IReadOnlyList<string> ReviewKeysInOrder = [DocumentChangeReview, StatementReview, ActionReview];

    internal static Task AcquireAsync(DbContext db, string key, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(AcquireSql, [new NpgsqlParameter("key", key)], cancellationToken);

    internal static Task<IDbContextTransaction> BeginWriteAsync(DbContext db, string key, CancellationToken cancellationToken) =>
        BeginWriteAsync(db, [key], cancellationToken);

    internal static async Task<IDbContextTransaction> BeginWriteAsync(DbContext db, IReadOnlyList<string> keysInOrder,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var key in keysInOrder) await AcquireAsync(db, key, cancellationToken);
        return transaction;
    }
}
