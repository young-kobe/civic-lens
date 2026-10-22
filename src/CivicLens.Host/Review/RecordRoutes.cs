using CivicLens.Host.Actions;
using CivicLens.Host.Briefs;
using CivicLens.Host.Documents;
using CivicLens.Host.Relationships;
using CivicLens.Host.Statements;
using CivicLens.Host.Votes;
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
