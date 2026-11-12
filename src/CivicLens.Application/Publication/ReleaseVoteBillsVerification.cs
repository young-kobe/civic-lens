namespace CivicLens.Application.Publication;

/// <param name="Passed">The kept releases whose stored vote links equal the expected links.</param>
/// <param name="Failed">The kept releases whose manifest cannot be read or whose stored links differ from the expected links.</param>
/// <param name="Retired">The release rows whose folder holds no manifest.</param>
public sealed record ReleaseVoteBillsVerification(int Passed, int Failed, int Retired, IReadOnlyDictionary<string, int> FailedByReason,
    IReadOnlyList<FailedRelease> FailedReleases);
