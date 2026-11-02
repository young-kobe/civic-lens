using System.Collections.Immutable;

namespace CivicLens.Application.Votes;

public interface IVoteRevisionBackfillStore
{
    /// <summary>Moves the next legacy revisions after the given key into columns and child rows. One call is one short transaction under the vote review lock.</summary>
    Task<VoteRevisionBackfillBatch> BackfillBatchAsync(VoteRevisionKey? after, int size, CancellationToken cancellationToken);

    /// <summary>Rebuilds each revision from its rows and compares it to its JSON. It writes nothing.</summary>
    Task<VoteRevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken);
}

public sealed record VoteRevisionKey(string RecordId, int RevisionNumber);

public sealed record SkippedVoteRevision(string RecordId, int RevisionNumber, string Reason);

public sealed record VoteRevisionBackfillBatch(int Examined, ImmutableArray<SkippedVoteRevision> Skipped, VoteRevisionKey? Last);

public sealed record VoteRevisionMismatch(string RecordId, int RevisionNumber, IReadOnlyList<string> Fields);

public sealed record VoteRevisionVerification(int Checked, int Legacy, int Mismatched, IReadOnlyList<VoteRevisionMismatch> FirstMismatches);
