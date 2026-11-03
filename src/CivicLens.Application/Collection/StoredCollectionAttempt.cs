using CivicLens.Core.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection;

/// <summary>Retained evidence for one attempt, without its per-call import disposition.</summary>
public sealed class StoredCollectionAttempt
{
    public StoredCollectionAttempt(CollectionAttemptResult attemptResult, SentValidators? sentValidators,
        CapturedAttemptResult? priorCapturedAttempt, DiscoveryEvidence? discovery = null, long? robotsCrawlDelayMilliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(attemptResult);
        if (attemptResult is not NotModifiedAttemptResult && priorCapturedAttempt is not null)
            throw new ArgumentException("Only a not-modified attempt can retain a prior capture link.", nameof(priorCapturedAttempt));
        if (discovery is not null && (attemptResult is not CapturedAttemptResult ||
            discovery.Request.SourceId != attemptResult.SourceId || discovery.Request.Url != attemptResult.RequestedUrl))
            throw new ArgumentException("Discovery must belong to the captured attempt.", nameof(discovery));
        if (robotsCrawlDelayMilliseconds is < 0 or > CollectionProtocol.MaximumCrawlDelayMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(robotsCrawlDelayMilliseconds));
        RobotsCrawlDelayMilliseconds = robotsCrawlDelayMilliseconds;
        Discovery = discovery;
        AttemptResult = attemptResult;
        SentValidators = sentValidators;
        PriorCapturedAttempt = priorCapturedAttempt;
        var decision = ToDecision(new CollectionImportPolicy());
        if (decision.PriorCapturedAttempt != priorCapturedAttempt)
            throw new ArgumentException("Retained prior capture link is not supported by the attempt and sent validators.",
                nameof(priorCapturedAttempt));
    }

    public long? RobotsCrawlDelayMilliseconds { get; }
    public DiscoveryEvidence? Discovery { get; }
    public CollectionAttemptResult AttemptResult { get; }
    public SentValidators? SentValidators { get; }
    public CapturedAttemptResult? PriorCapturedAttempt { get; }

    internal CollectionImportDecision ToDecision(CollectionImportPolicy policy) =>
        policy.Decide(AttemptResult, priorCapturedAttempt: PriorCapturedAttempt, sentValidators: SentValidators);
}
