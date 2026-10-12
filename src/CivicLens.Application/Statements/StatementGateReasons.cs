namespace CivicLens.Application.Statements;

public static class StatementGateReasons
{
    public const string Passed = "passed";
    public const string NoTrackedIssue = "no-tracked-issue";
    public const string OverByteCap = "over-byte-cap";
    public const string FailedOpen = "gate-failed-open";

    public static bool IsKnown(string? reason) => reason is Passed or NoTrackedIssue or OverByteCap or FailedOpen;

    public static bool QueuesDraft(string? reason) => reason is Passed or OverByteCap or FailedOpen;
}
