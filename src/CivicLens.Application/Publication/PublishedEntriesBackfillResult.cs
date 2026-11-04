namespace CivicLens.Application.Publication;

/// <param name="Examined">The revision ranges that had no entry row when the run began.</param>
/// <param name="Added">The rows that this run inserted.</param>
/// <param name="AlreadyPresent">The ranges that already had a row, including rows that a commit inserted during the run.</param>
/// <param name="OpenRangesWithoutRow">The open ranges that still have no row at the end of the run.</param>
/// <param name="ClosedRangesWithoutKeptFolder">The closed ranges that no kept folder lists. They get no row.</param>
public sealed record PublishedEntriesBackfillResult(int Examined, int Added, int AlreadyPresent, int Skipped,
    IReadOnlyDictionary<string, int> SkippedByReason, IReadOnlyList<SkippedPublishedEntry> SkippedEntries,
    int OpenRangesWithoutRow, int ClosedRangesWithoutKeptFolder);
