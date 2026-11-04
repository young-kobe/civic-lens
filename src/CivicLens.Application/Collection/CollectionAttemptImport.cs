using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection;

/// <summary>A validated collection attempt ready for an atomic evidence import.</summary>
public sealed class CollectionAttemptImport
{
    private readonly CollectionImportPolicy policy = new();

    internal CollectionAttemptImport(CollectionRequest request, CollectionAttemptResult attemptResult,
        SentValidators? sentValidators, long elapsedMilliseconds, DiscoveryEvidence? discovery = null,
        long? robotsCrawlDelayMilliseconds = null, AttemptTransfer? transfer = null)
    {
        Request = request;
        ElapsedMilliseconds = elapsedMilliseconds;
        AttemptResult = attemptResult;
        SentValidators = sentValidators;
        Discovery = discovery;
        RobotsCrawlDelayMilliseconds = robotsCrawlDelayMilliseconds;
        Transfer = transfer;
    }

    public CollectionRequest Request { get; }
    public long ElapsedMilliseconds { get; }
    public long? RobotsCrawlDelayMilliseconds { get; }
    public AttemptTransfer? Transfer { get; }
    public CollectionAttemptResult AttemptResult { get; }
    public SentValidators? SentValidators { get; }
    public DiscoveryEvidence? Discovery { get; }

    /// <summary>Decides against state loaded in the same atomic persistence operation.</summary>
    public CollectionImportDecision Decide(StoredCollectionAttempt? existingAttempt,
        CapturedAttemptResult? priorCapturedAttempt) =>
        DecideFromCandidates(existingAttempt, priorCapturedAttempt is null ? [] : [priorCapturedAttempt]);

    /// <summary>Decides against all potentially eligible captures, retaining no link when their evidence is ambiguous.</summary>
    public CollectionImportDecision DecideFromCandidates(StoredCollectionAttempt? existingAttempt,
        IEnumerable<CapturedAttemptResult> priorCapturedAttempts) =>
        DecideWithDiscovery(existingAttempt, priorCapturedAttempts);

    private CollectionImportDecision DecideWithDiscovery(StoredCollectionAttempt? existing,
        IEnumerable<CapturedAttemptResult> priorCaptures)
    {
        if (existing is not null && !(Discovery is null ? existing.Discovery is null : Discovery.Matches(existing.Discovery)))
            throw new InvalidOperationException("An attempt ID was reused with conflicting discovery evidence.");
        if (existing is not null && existing.RobotsCrawlDelayMilliseconds != RobotsCrawlDelayMilliseconds)
            throw new InvalidOperationException("An attempt ID was reused with conflicting robots crawl delay.");
        if (existing is not null && existing.ElapsedMilliseconds != ElapsedMilliseconds)
            throw new InvalidOperationException("An attempt ID was reused with conflicting elapsed time.");
        return policy.DecideFromCandidates(AttemptResult, priorCaptures, existing?.ToDecision(policy), SentValidators);
    }
}
