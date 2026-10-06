using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

/// <summary>A validated collection attempt ready for an atomic evidence import.</summary>
public sealed class CollectionAttemptImport
{
    private readonly CollectionImportPolicy policy = new();

    internal CollectionAttemptImport(CollectionAttemptResult attemptResult, SentValidators? sentValidators)
    {
        AttemptResult = attemptResult;
        SentValidators = sentValidators;
    }

    public CollectionAttemptResult AttemptResult { get; }
    public SentValidators? SentValidators { get; }

    /// <summary>Decides against state loaded in the same atomic persistence operation.</summary>
    public CollectionImportDecision Decide(StoredCollectionAttempt? existingAttempt,
        CapturedAttemptResult? priorCapturedAttempt) =>
        policy.Decide(AttemptResult, existingAttempt?.ToDecision(policy), priorCapturedAttempt, SentValidators);

    /// <summary>Decides against all potentially eligible captures, retaining no link when their evidence is ambiguous.</summary>
    public CollectionImportDecision DecideFromCandidates(StoredCollectionAttempt? existingAttempt,
        IEnumerable<CapturedAttemptResult> priorCapturedAttempts) =>
        policy.DecideFromCandidates(AttemptResult, priorCapturedAttempts, existingAttempt?.ToDecision(policy), SentValidators);
}
