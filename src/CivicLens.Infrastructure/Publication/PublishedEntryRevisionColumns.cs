using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Publication;

internal static class PublishedEntryRevisionColumns
{
    private const string RevisionsSuffix = "_revisions";

    public static (string RecordId, string RevisionNumber) For(PublishedRecordKind kind)
    {
        var (revisions, _, idColumn) = ReviewTables.For(kind);
        var prefix = revisions[..^RevisionsSuffix.Length];
        return ($"{prefix}_{idColumn}", $"{prefix}_revision_number");
    }
}
