using CivicLens.Host.Actions;
using CivicLens.Host.Briefs;
using CivicLens.Host.Documents;
using CivicLens.Host.ExecutiveActions;
using CivicLens.Host.Relationships;
using CivicLens.Host.Statements;
using CivicLens.Host.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Review;

internal static class RecordRoutes
{
    public static string Editor(PublishedRecordKind kind, string recordId, string? status = null) => kind switch
    {
        PublishedRecordKind.DocumentChange => DocumentRoutes.Editor(recordId, status),
        PublishedRecordKind.Statement => StatementRoutes.Editor(recordId, status),
        PublishedRecordKind.Action => ActionRoutes.Editor(recordId, status),
        PublishedRecordKind.Vote => VoteRoutes.Editor(recordId, status),
        PublishedRecordKind.Brief => BriefRoutes.Editor(recordId, status),
        PublishedRecordKind.Relationship => RelationshipRoutes.Editor(recordId, status),
        PublishedRecordKind.ExecutiveAction => ExecutiveActionRoutes.Editor(recordId, status),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no review page.")
    };
}
