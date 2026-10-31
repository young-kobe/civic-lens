using CivicLens.Core.Relationships;
using CivicLens.Host.Actions;
using CivicLens.Host.Votes;

namespace CivicLens.Host.Relationships;

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

    public const string NoLongerMatches = "This statement and this action or vote no longer share a bill.";

    public const string CheckBeforeApproving = "Check the link before you approve it.";

    public const string CheckApprovedLink =
        "Open the link and check it. Withdraw the approval if it is wrong. If it is right, it stays on the site and this row stays here.";

    public static string Title(RelationshipType type, string statementHeadline) => $"{Type(type)}: {statementHeadline}";

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
