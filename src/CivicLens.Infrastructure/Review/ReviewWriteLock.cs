using System.Data;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewWriteLock
{
    public const string DocumentChange = "civic-lens-document-change-review";
    public const string Statement = "civic-lens-statement-review";

    public static readonly IReadOnlyList<string> AllInOrder = [DocumentChange, Statement];

    public static Task AcquireAsync(CollectionAttemptDbContext db, string key, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

    public static Task<IDbContextTransaction> BeginWriteAsync(CollectionAttemptDbContext db, string key,
        CancellationToken cancellationToken) =>
        BeginWriteAsync(db, [key], cancellationToken);

    public static async Task<IDbContextTransaction> BeginWriteAsync(CollectionAttemptDbContext db, IReadOnlyList<string> keysInOrder,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var key in keysInOrder) await AcquireAsync(db, key, cancellationToken);
        return transaction;
    }
}
