using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns the rule that the state rows of a release and the entries of its manifest name the same revisions, and that each state row has its entry row.</summary>
internal static class ReleaseMembership
{
    public const string EntryRowMissing = "entryRowMissing";
    public const string FirstPublishedMissing = "firstPublishedMissing";
    public const string StateRowNotListed = "stateRowNotListed";
    public const string EntryOutsideRange = "entryOutsideRange";

    public static (string Reason, PublishedRecordReference Record)? FindBreak(PublicationRelease file, StoredRelease stored)
    {
        var listed = file.Records.ToDictionary(entry => new PublishedRecordReference(entry.Kind, entry.RecordId));
        var covered = stored.Records.ToDictionary(row => new PublishedRecordReference(row.Kind, row.RecordId));
        foreach (var (reference, row) in covered)
        {
            if (row.Facts is null) return (EntryRowMissing, reference);
            if (row.FirstPublishedAtUtc is null) return (FirstPublishedMissing, reference);
            if (!listed.TryGetValue(reference, out var entry) || entry.RevisionNumber != row.RevisionNumber) return (StateRowNotListed, reference);
        }
        foreach (var (reference, entry) in listed)
            if (!covered.TryGetValue(reference, out var row) || row.RevisionNumber != entry.RevisionNumber) return (EntryOutsideRange, reference);
        return null;
    }
}
