namespace CivicLens.Application.Actions;

/// <summary>The run stopped on a cancel. The caller already holds the report of the bills that finished.
/// <see cref="ChargeFailure"/> is set when the cost of the run could not be added to the month.</summary>
public sealed class ActionTextMeasureCanceledException(OperationCanceledException inner, Exception? chargeFailure)
    : OperationCanceledException(inner.Message, inner, inner.CancellationToken)
{
    public Exception? ChargeFailure { get; } = chargeFailure;
}
