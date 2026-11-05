namespace CivicLens.Application.Publication;

/// <param name="Passed">The kept releases that the tables rebuild byte for byte.</param>
/// <param name="Legacy">The kept releases that pass, but list withdrawals that no withdrawal row covers. Their withdrawals come from the file.</param>
/// <param name="Failed">The kept releases that fail a check.</param>
/// <param name="Retired">The release rows whose folder holds no manifest.</param>
public sealed record PublishedEntriesVerification(int Passed, int Legacy, int Failed, int Retired, IReadOnlyDictionary<string, int> FailedByReason,
    IReadOnlyList<FailedRelease> FailedReleases);
