using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public interface IPublicationStore
{
    Task<PublicationState> GetStateAsync(CancellationToken cancellationToken);
    Task<TimeSpan?> ReadAgeOfNewestReleaseAsync(CancellationToken cancellationToken);
    Task<int> CountApprovedOutsideActiveReleaseAsync(IReadOnlyCollection<PublishedRecordReference> withdrawn, CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken);
    Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, Func<string, CancellationToken, Task> requireFiles,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken);

    Task PruneAsync(Func<ReleaseRetentionPlan, string?, CancellationToken, Task> retire, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PublishedRecordReference, ReviewedRecord>> ReadReviewStatusesAsync(
        IReadOnlyCollection<PublishedRevisionBinding> published, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset>> ReadFirstPublishedAsync(
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken);

    Task<int> AddFirstPublishedAsync(IReadOnlyCollection<PublishedFirstPublication> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicationReleaseSummary>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PublishedEntryCandidate>> ReadEntryCandidatesAsync(CancellationToken cancellationToken);

    Task<StoredRelease> ReadReleaseAsync(int releaseNumber, CancellationToken cancellationToken);

    Task<int> AddEntriesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken);

    Task<int> FillBillLinesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken);

    /// <summary>Lists the published vote revisions with a member list whose stored vote columns are not written yet.</summary>
    Task<IReadOnlyList<VoteColumnGap>> ReadVoteColumnGapsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the vote columns of revisions that are not written yet. A member list keeps the parties it already holds. A call that pins two different party sets
    /// on one list fails, and it writes nothing.
    /// </summary>
    Task WriteVoteColumnsAsync(IReadOnlyCollection<VoteStoredColumns> columns, CancellationToken cancellationToken);

    Task<int> FillOfficialPhotoPathsAsync(int releaseNumber, IReadOnlyDictionary<string, string> photoPaths, CancellationToken cancellationToken);

    /// <summary>Reads up to <paramref name="limit"/> entry rows with no Congress that follow <paramref name="after"/> in key order.</summary>
    Task<IReadOnlyList<PublishedEntryDay>> ReadEntryDaysWithoutCongressAsync(PublishedEntryDay? after, int limit, CancellationToken cancellationToken);

    Task<int> FillEntryCongressesAsync(IReadOnlyCollection<PublishedEntryDay> entries, CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);
}
