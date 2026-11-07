using System.Linq.Expressions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

internal sealed record RevisionBackfillKind<TRow, TRevision, TPage>(
    string Savepoint,
    string LockKey,
    Expression<Func<TRow, bool>> IsLegacy,
    Func<CollectionAttemptDbContext, IQueryable<TRow>, CancellationToken, Task<TPage>> ReadPageAsync,
    Func<CollectionAttemptDbContext, TRow, TRevision, CancellationToken, Task> FillColumnsAsync,
    Func<TRow, (TRevision? Revision, string? Refusal)> ReadMovable,
    Func<TRow, TPage, IReadOnlyList<string>> DifferingFields,
    string RecordIdProperty = nameof(IReviewRevisionRow.RecordId),
    Func<TRow, TRow, bool>? DerivedValuesMatch = null);
