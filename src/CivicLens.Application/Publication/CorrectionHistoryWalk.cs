namespace CivicLens.Application.Publication;

/// <summary>Owns which selected release a verification cursor continues in, and what the walk reports when the cursor release is gone.</summary>
public static class CorrectionHistoryWalk
{
    public static CorrectionHistoryWalkStep Resolve(IReadOnlyList<PublicationReleaseSummary> selection,
        CorrectionHistoryVerificationCursor? cursor)
    {
        if (cursor is null) return StepAt(selection, selection.FirstOrDefault(), null, null);
        var named = selection.FirstOrDefault(release => release.ReleaseNumber == cursor.ReleaseNumber);
        if (named is not null && named.DirectoryName == cursor.DirectoryName) return StepAt(selection, named, cursor.After, null);
        var skipped = new CorrectionHistorySkippedRelease(cursor.ReleaseNumber, cursor.DirectoryName,
            named is null ? CorrectionHistorySkippedRelease.NotSelected : CorrectionHistorySkippedRelease.DirectoryChanged);
        return StepAt(selection, selection.FirstOrDefault(release => release.ReleaseNumber > cursor.ReleaseNumber), null, skipped);
    }

    private static CorrectionHistoryWalkStep StepAt(IReadOnlyList<PublicationReleaseSummary> selection, PublicationReleaseSummary? target,
        PublishedRecordReference? after, CorrectionHistorySkippedRelease? skipped) =>
        new(target, after, target is null ? null : selection.FirstOrDefault(release => release.ReleaseNumber > target.ReleaseNumber), skipped);
}
