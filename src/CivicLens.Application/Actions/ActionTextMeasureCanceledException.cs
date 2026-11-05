namespace CivicLens.Application.Actions;

/// <summary>The run stopped on a cancel. It holds the bills that finished, so the caller can still write them.</summary>
public sealed class ActionTextMeasureCanceledException(ActionTextMeasureReport report, OperationCanceledException inner)
    : OperationCanceledException(inner.Message, inner, inner.CancellationToken)
{
    public ActionTextMeasureReport Report { get; } = report;
}
