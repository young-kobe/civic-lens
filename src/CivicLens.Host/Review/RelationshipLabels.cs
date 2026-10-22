using CivicLens.Core.Relationships;

namespace CivicLens.Host.Review;

internal static class RelationshipLabels
{
    public static string Type(RelationshipType type) => type switch
    {
        RelationshipType.DescribesOwnBill => "Describes own bill",
        RelationshipType.StatesPositionOn => "States position on",
        RelationshipType.ClaimsCreditFor => "Claims credit for",
        RelationshipType.StatementAboutVote => "Statement about a vote",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Relationship type has no label.")
    };

    public static string Basis(RelationshipMatchBasis basis) => basis == RelationshipMatchBasis.BillNumber ? "Bill number" : "Bill title";

    public static string Summary(RelationshipRevision revision) => $"{Type(revision.Type)}: {revision.Reason}";

    public static string TargetEditor(RelationshipCitation target) => target.Kind switch
    {
        RelationshipRecordKind.Action => ActionRoutes.Editor(target.RecordId),
        RelationshipRecordKind.Vote => VoteRoutes.Editor(target.RecordId),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target.Kind, "Relationship target kind has no editor.")
    };

    public static string TargetName(RelationshipRecordKind kind) => kind switch
    {
        RelationshipRecordKind.Action => "Action",
        RelationshipRecordKind.Vote => "Vote",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Relationship target kind has no name.")
    };
}
