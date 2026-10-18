namespace CivicLens.Application.Collection.Bills;

public readonly record struct BillGateOutcome(BillGateDecision Decision, BillGateReason? Reason, bool HasTrackedIssue = false)
{
    public static BillGateOutcome Pending { get; } = new(BillGateDecision.Pending, null);

    public static BillGateOutcome Passed(BillGateReason reason) => new(BillGateDecision.Passed, reason);

    public static BillGateOutcome NotRelevant(BillGateReason reason) => new(BillGateDecision.NotRelevant, reason);

    public bool IsPending => Decision == BillGateDecision.Pending;
    public bool IsPassed => Decision == BillGateDecision.Passed;
}
