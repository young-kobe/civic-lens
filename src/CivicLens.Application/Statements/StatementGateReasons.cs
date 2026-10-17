using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public static class StatementGateReasons
{
    public const string Passed = "passed";
    public const string PassedBillOrVote = "passed-bill-or-vote";
    public const string PassedBillNumber = "passed-bill-number";
    public const string NoTrackedIssue = "no-tracked-issue";
    public const string OverByteCap = "over-byte-cap";
    public const string FailedOpen = "gate-failed-open";

    public static bool IsKnown(string? reason) => IsPass(reason) || reason is NoTrackedIssue or OverByteCap or FailedOpen;

    public static readonly ImmutableArray<string> PassReasons = [Passed, PassedBillOrVote, PassedBillNumber];

    public static bool IsPass(string? reason) => reason is not null && PassReasons.Contains(reason);

    public static bool QueuesDraft(string? reason) => IsPass(reason) || reason is OverByteCap or FailedOpen;
}
