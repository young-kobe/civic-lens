using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>What the tables hold for one release: the state rows that cover it and the withdrawal rows that cover it.</summary>
public sealed record StoredRelease(IReadOnlyList<StoredReleaseRecord> Records, IReadOnlyList<PublishedWithdrawal> Withdrawals);
