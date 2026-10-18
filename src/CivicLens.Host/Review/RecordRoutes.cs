using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Review;

internal static class RecordRoutes
{
    public static string Editor(PublishedRecordKind kind, string recordId) => kind switch
    {
        PublishedRecordKind.DocumentChange => DocumentRoutes.Editor(recordId),
        PublishedRecordKind.Statement => StatementRoutes.Editor(recordId),
        PublishedRecordKind.Action => ActionRoutes.Editor(recordId),
        PublishedRecordKind.Vote => VoteRoutes.Editor(recordId),
        PublishedRecordKind.Brief => BriefRoutes.Editor(recordId),
        PublishedRecordKind.Relationship => RelationshipRoutes.Editor(recordId),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no review page.")
    };
}
