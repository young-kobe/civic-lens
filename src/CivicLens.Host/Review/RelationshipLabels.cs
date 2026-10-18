using CivicLens.Core.Relationships;

namespace CivicLens.Host.Review;

internal static class RelationshipLabels
{
    public static string Type(RelationshipType type) => type switch
    {
        RelationshipType.DescribesOwnBill => "Describes own bill",
        RelationshipType.StatesPositionOn => "States position on",
        RelationshipType.ClaimsCreditFor => "Claims credit for",
        _ => "Statement about a vote"
    };

    public static string Basis(RelationshipMatchBasis basis) => basis == RelationshipMatchBasis.BillNumber ? "Bill number" : "Bill title";

    public static string Summary(RelationshipRevision revision) => $"{Type(revision.Type)}: {revision.Reason}";

    public static string TargetEditor(RelationshipCitation target) => target.Kind == RelationshipRecordKind.Action
        ? ActionRoutes.Editor(target.RecordId) : VoteRoutes.Editor(target.RecordId);

    public static string TargetName(RelationshipRecordKind kind) => kind == RelationshipRecordKind.Action ? "Action" : "Vote";
}
