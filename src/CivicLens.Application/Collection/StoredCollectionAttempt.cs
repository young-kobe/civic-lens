using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

/// <summary>Retained evidence for one attempt, without its per-call import disposition.</summary>
public sealed class StoredCollectionAttempt
{
    public StoredCollectionAttempt(CollectionAttemptResult attemptResult, SentValidators? sentValidators,
        CapturedAttemptResult? priorCapturedAttempt)
    {
        ArgumentNullException.ThrowIfNull(attemptResult);
        if (attemptResult is not NotModifiedAttemptResult && priorCapturedAttempt is not null)
            throw new ArgumentException("Only a not-modified attempt can retain a prior capture link.", nameof(priorCapturedAttempt));
        AttemptResult = attemptResult;
        SentValidators = sentValidators;
        PriorCapturedAttempt = priorCapturedAttempt;
        var decision = ToDecision(new CollectionImportPolicy());
        if (decision.PriorCapturedAttempt != priorCapturedAttempt)
            throw new ArgumentException("Retained prior capture link is not supported by the attempt and sent validators.",
                nameof(priorCapturedAttempt));
    }

    public CollectionAttemptResult AttemptResult { get; }
    public SentValidators? SentValidators { get; }
    public CapturedAttemptResult? PriorCapturedAttempt { get; }

    internal CollectionImportDecision ToDecision(CollectionImportPolicy policy) =>
        policy.Decide(AttemptResult, priorCapturedAttempt: PriorCapturedAttempt, sentValidators: SentValidators);
}
