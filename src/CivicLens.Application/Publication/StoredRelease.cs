using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// What the tables hold for one release: the state rows that cover it and the withdrawal rows that cover it.
/// <paramref name="PredatesWithdrawalRows"/> is true when no withdrawal row starts at this release or earlier, so the release has no withdrawal rows by age.
/// </summary>
public sealed record StoredRelease(IReadOnlyList<StoredReleaseRecord> Records, IReadOnlyList<PublishedWithdrawal> Withdrawals, bool PredatesWithdrawalRows);
