using CivicLens.Application.Publication;
using CivicLens.Core.Votes;
using CivicLens.Host.Publication;

namespace CivicLens.Host.Review;

internal static class VoteLabels
{
    public static string RollCall(VoteDraftRevision revision) =>
        RollCall(revision.Chamber, revision.Congress, revision.SessionNumber, revision.RollCallNumber);

    public static string RollCall(Chamber chamber, int congress, int sessionNumber, int rollCallNumber) =>
        $"{chamber} roll call {rollCallNumber}, Congress {congress}, session {sessionNumber}";

    public static string RollCallSite(Chamber chamber) => chamber == Chamber.Senate ? "senate.gov" : "clerk.house.gov";

    public static string Bill(VoteDraftRevision revision) =>
        revision.BillType is null ? "None" : ActionLabels.Bill(revision.Congress, revision.BillType, revision.BillNumber!.Value);

    public static string Kind(VoteKind kind) => PublishedRecordLabels.VoteKind(PublishedVoteKinds.Of(kind));
}
