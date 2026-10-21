using CivicLens.Core.Votes;

namespace CivicLens.Host.Review;

internal static class VoteLabels
{
    public static string RollCall(VoteDraftRevision revision) =>
        RollCall(revision.Chamber, revision.Congress, revision.SessionNumber, revision.RollCallNumber);

    public static string RollCall(Chamber chamber, int congress, int sessionNumber, int rollCallNumber) =>
        $"{chamber} roll call {rollCallNumber}, Congress {congress}, session {sessionNumber}";

    public static string Bill(VoteDraftRevision revision) =>
        revision.BillType is null ? "None" : ActionLabels.Bill(revision.Congress, revision.BillType, revision.BillNumber!.Value);

    public static string Kind(VoteKind kind) => kind switch
    {
        VoteKind.Rule => "Rule",
        VoteKind.PreviousQuestion => "Previous question",
        VoteKind.MotionToRecommit => "Motion to recommit",
        VoteKind.Amendment => "Amendment",
        VoteKind.Suspension => "Suspension",
        VoteKind.Passage => "Passage",
        VoteKind.ConferenceReport => "Conference report",
        VoteKind.VetoOverride => "Veto override",
        _ => "Other"
    };
}
