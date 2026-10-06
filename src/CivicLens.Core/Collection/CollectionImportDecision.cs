namespace CivicLens.Core.Collection;

public enum ImportDisposition
{
    NewAttempt,
    DuplicateAttempt
}

public enum PriorCaptureLinkStatus
{
    NotApplicable,
    Unresolved,
    Linked
}

/// <summary>Pure import decision. The result describes the completed attempt; a 304 links separately to a prior captured attempt.</summary>
public sealed class CollectionImportDecision
{
    internal CollectionImportDecision(CollectionAttemptResult attemptResult, CapturedAttemptResult? priorCapturedAttempt,
        ImportDisposition disposition, SentValidators? sentValidators)
    {
        AttemptResult = attemptResult;
        PriorCapturedAttempt = priorCapturedAttempt;
        Disposition = disposition;
        SentValidators = sentValidators;
        PriorCaptureLinkStatus = attemptResult is not NotModifiedAttemptResult
            ? PriorCaptureLinkStatus.NotApplicable
            : priorCapturedAttempt is null ? PriorCaptureLinkStatus.Unresolved : PriorCaptureLinkStatus.Linked;
    }

    public CollectionAttemptResult AttemptResult { get; }
    public CapturedAttemptResult? PriorCapturedAttempt { get; }
    public ImportDisposition Disposition { get; }
    public PriorCaptureLinkStatus PriorCaptureLinkStatus { get; }
    public SentValidators? SentValidators { get; }
    public CaptureIdentity? Capture => AttemptResult is CapturedAttemptResult captured
        ? captured.Capture
        : PriorCapturedAttempt?.Capture;
}
