using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewIdempotency
{
    public static void ValidateRequest(string actorSubject, string idempotencyKey, string payloadHash)
    {
        if (!ReviewAuthor.IsValidSubject(actorSubject))
            throw new ArgumentException("Actor subject is invalid.", nameof(actorSubject));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256)
            throw new ArgumentException("Idempotency key is invalid.", nameof(idempotencyKey));
        if (payloadHash is not { Length: 64 } || !payloadHash.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException("Payload hash must be a lowercase SHA-256 value.", nameof(payloadHash));
    }

    public static async Task<T> WriteOnceAsync<T>(CollectionAttemptDbContext db, string lockKey, string actorSubject, string operation,
        string idempotencyKey, string payloadHash, Func<Task<T>> write, CancellationToken cancellationToken) where T : class
    {
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, lockKey, cancellationToken);
        var replay = await ReadReplayAsync<T>(db, actorSubject, operation, idempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var result = await write();
        Add(db, actorSubject, operation, idempotencyKey, payloadHash, ReviewJson.Serialize(result));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public static async Task<T?> ReadReplayAsync<T>(CollectionAttemptDbContext db, string actorSubject, string operation,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) where T : class
    {
        var row = await db.Set<ReviewIdempotencyRow>().AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.ActorSubject == actorSubject && candidate.Operation == operation &&
            candidate.IdempotencyKey == idempotencyKey, cancellationToken);
        if (row is null) return null;
        if (row.PayloadHash != payloadHash)
            throw new ArgumentException("Idempotency key was already used with a different request payload.");
        return ReviewJson.Deserialize<T>(row.ResultJson, "idempotency result");
    }

    public static void Add(CollectionAttemptDbContext db, string actorSubject, string operation, string idempotencyKey,
        string payloadHash, string resultJson) => db.Add(new ReviewIdempotencyRow
        {
            ActorSubject = actorSubject,
            Operation = operation,
            IdempotencyKey = idempotencyKey,
            PayloadHash = payloadHash,
            ResultJson = resultJson
        });
}
