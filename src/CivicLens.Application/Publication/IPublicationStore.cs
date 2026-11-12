using System.Collections.Immutable;
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

    Task<int> FillStatementApproversAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken);
    Task<int> FillApprovalTimesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken);

    Task<int> AddBillStatusesAsync(int releaseNumber, IReadOnlyCollection<PublishedBillStatus> statuses, CancellationToken cancellationToken);

    /// <summary>Adds the vote links of one release and returns the number of rows added.</summary>
    Task<int> AddVoteBillsAsync(int releaseNumber, IReadOnlyCollection<ReleaseVoteBill> voteBills, CancellationToken cancellationToken);

    /// <summary>The vote links that the store holds for one release.</summary>
    Task<IReadOnlyList<ReleaseVoteBill>> ReadVoteBillsAsync(int releaseNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillPublicationInput>> ReadBillInputsAsync(int? previousReleaseNumber, ImmutableArray<string> recordIds, CancellationToken cancellationToken);

    Task PrepareBillHistoriesAsync(IReadOnlyCollection<BillHistoryCapture> histories, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PublishedRecordReference, StoredCorrectionHistory>> ReadCorrectionHistoriesAsync(int releaseNumber,
        IReadOnlyCollection<PublishedRevisionBinding> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<CorrectionHistoryBinding>> PrepareCorrectionHistoriesAsync(
        IReadOnlyCollection<PublicationCorrectionHistory> histories, CancellationToken cancellationToken);

    Task<CorrectionHistoryBootstrapPage> ReadCorrectionHistoryBootstrapPageAsync(CorrectionHistoryBootstrapCursor? after, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    Task<CorrectionHistoryVerificationPage> ReadCorrectionHistoryVerificationPageAsync(CorrectionHistoryVerificationCursor? after, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicationReleaseSummary>> ReadRetainedReleaseSelectionAsync(Func<CancellationToken, Task<string?>> readServedDirectory,
        CancellationToken cancellationToken);

    Task<string> ReadRetainedReleaseFingerprintAsync(Func<CancellationToken, Task<string?>> readServedDirectory,
        CancellationToken cancellationToken);

    Task<(int Added, int AlreadyPresent)> CommitCorrectionHistoryBootstrapAsync(PublicationReleaseSummary release,
        IReadOnlyCollection<CorrectionHistoryBinding> bindings, Func<CancellationToken, Task<string?>> readServedDirectory,
        CancellationToken cancellationToken);

    /// <summary>Reads the next retained, active, or served releases after <paramref name="afterReleaseNumber"/>. A fingerprint that differs from the current selection marks the page stale.</summary>
    Task<BillCountReleasePage> ReadBillCountReleasePageAsync(int afterReleaseNumber, string? selectionFingerprint, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    Task<ReleaseBillCountSource> ReadBillCountSourceAsync(int releaseNumber, CancellationToken cancellationToken);

    /// <summary>Writes the bill counts of one release in one transaction. It reports an exact existing snapshot as already present.</summary>
    Task<ReleaseSnapshotWrite> CommitBillCountsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    Task<BillCountStatus> VerifyBillCountsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    /// <summary>Writes the party line-up of one release in one transaction. It reports a release that needs no rows apart from one that already holds an exact snapshot.</summary>
    Task<ReleaseSnapshotWrite> CommitPartyLineUpsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    Task<BillCountStatus> VerifyPartyLineUpsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken);

    /// <summary>Reads, in one read-only transaction, the next vote revisions after <paramref name="after"/> with their stored tally groups and the release range of their record states.</summary>
    Task<VotePartyTallyPage> ReadVotePartyTallyEvidenceAsync(VotePartyTallyVerificationCursor? after, int limit, CancellationToken cancellationToken);

    Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken);

    Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken);
}
