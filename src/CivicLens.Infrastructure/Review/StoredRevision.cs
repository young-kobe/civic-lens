namespace CivicLens.Infrastructure.Review;

/// <summary>A revision row and the read that builds its revision. The read runs later, so one bad row fails only its own draft.</summary>
internal sealed record StoredRevision<TRevisionRow, TRevision>(TRevisionRow Row, Func<TRevision> Read);
