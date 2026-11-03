namespace CivicLens.Application.Review;

public interface IRevisionBackfillStore
{
    /// <summary>Examines the next legacy revisions after the given key, at most the given number, and moves each into columns and child rows. One call is one short transaction under the review lock of the kind.</summary>
    Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken);

    /// <summary>Rebuilds each revision from its rows and compares it to its JSON. It writes nothing.</summary>
    Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken);
}
