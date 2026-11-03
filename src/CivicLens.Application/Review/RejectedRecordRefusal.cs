namespace CivicLens.Application.Review;

/// <summary>The reason that a run keeps when the worker refused an update because its record was rejected. It records what happened.
/// The block that stops an approval lives in the column <c>refused_candidate_id</c> of the draft.</summary>
public static class RejectedRecordRefusal
{
    public const string Reason = "recordRejected";
}
