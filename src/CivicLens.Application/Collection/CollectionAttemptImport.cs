using CivicLens.Core.Collection;
using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection;

/// <summary>A validated collection attempt ready for an atomic evidence import.</summary>
public sealed class CollectionAttemptImport
{
    private readonly CollectionImportPolicy policy = new();

    internal CollectionAttemptImport(CollectionAttemptResult attemptResult, SentValidators? sentValidators,
        FeedDiscoveryEvidence? discovery = null)
    {
        AttemptResult = attemptResult;
        SentValidators = sentValidators;
        Discovery = discovery;
    }

    public CollectionAttemptResult AttemptResult { get; }
    public SentValidators? SentValidators { get; }
    public FeedDiscoveryEvidence? Discovery { get; }

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
        return policy.DecideFromCandidates(AttemptResult, priorCaptures, existing?.ToDecision(policy), SentValidators);
    }
}
