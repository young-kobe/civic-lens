namespace CivicLens.Application.Votes;

/// <summary>The run stopped on a cancel. It holds the roll calls that finished, so the caller can still write them.</summary>
public sealed class VoteDecidesMeasureCanceledException(VoteDecidesMeasureReport report, OperationCanceledException inner)
    : OperationCanceledException(inner.Message, inner, inner.CancellationToken)
{
    public VoteDecidesMeasureReport Report { get; } = report;
}
