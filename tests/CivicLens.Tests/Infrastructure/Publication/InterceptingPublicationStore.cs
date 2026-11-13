using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Infrastructure.Publication;

/// <summary>Wraps a store so that a test can run code before the release list, after the candidate read, and before each batch insert.</summary>
internal sealed class InterceptingPublicationStore(IPublicationStore inner) : IPublicationStore
{
    private int batches;

    public Func<Task>? AfterReadingCandidates { get; set; }

    public Func<Task>? BeforeListingAll { get; set; }

    public Func<int, Task>? BeforeAddingEntries { get; set; }

    public async Task<IReadOnlyList<PublishedEntryCandidate>> ReadEntryCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = await inner.ReadEntryCandidatesAsync(cancellationToken);
        if (AfterReadingCandidates is not null) await AfterReadingCandidates();
        return candidates;
    }

    public async Task<int> AddEntriesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken)
    {
        if (BeforeAddingEntries is not null) await BeforeAddingEntries(++batches);
        return await inner.AddEntriesAsync(entries, cancellationToken);
    }

    public Task<int> FillBillLinesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        inner.FillBillLinesAsync(entries, cancellationToken);

    public Task<IReadOnlyList<VoteColumnGap>> ReadVoteColumnGapsAsync(CancellationToken cancellationToken) => inner.ReadVoteColumnGapsAsync(cancellationToken);

    public Task WriteVoteColumnsAsync(IReadOnlyCollection<VoteStoredColumns> columns, CancellationToken cancellationToken) =>
        inner.WriteVoteColumnsAsync(columns, cancellationToken);

    public Task<int> FillOfficialPhotoPathsAsync(int releaseNumber, IReadOnlyDictionary<string, string> photoPaths, CancellationToken cancellationToken) =>
        inner.FillOfficialPhotoPathsAsync(releaseNumber, photoPaths, cancellationToken);

    public Task<IReadOnlyList<PublishedEntryDay>> ReadEntryDaysWithoutCongressAsync(PublishedEntryDay? after, int limit, CancellationToken cancellationToken) =>
        inner.ReadEntryDaysWithoutCongressAsync(after, limit, cancellationToken);

    public Task<int> FillEntryCongressesAsync(IReadOnlyCollection<PublishedEntryDay> entries, CancellationToken cancellationToken) =>
        inner.FillEntryCongressesAsync(entries, cancellationToken);

    public Task<int> FillApproversAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        inner.FillApproversAsync(entries, cancellationToken);

    public Task<int> FillApprovalTimesAsync(IReadOnlyCollection<PublishedRecordEntry> entries, CancellationToken cancellationToken) =>
        inner.FillApprovalTimesAsync(entries, cancellationToken);

    public Task<int> AddBillStatusesAsync(int releaseNumber, IReadOnlyCollection<PublishedBillStatus> statuses, CancellationToken cancellationToken) =>
        inner.AddBillStatusesAsync(releaseNumber, statuses, cancellationToken);

    public Task<IReadOnlyList<BillStatusGap>> ReadBillStatusGapsAsync(BillStatusGap? after, int limit, CancellationToken cancellationToken) =>
        inner.ReadBillStatusGapsAsync(after, limit, cancellationToken);

    public Task<int> FillBillStatusStagesAsync(IReadOnlyCollection<BillStatusGap> gaps, CancellationToken cancellationToken) =>
        inner.FillBillStatusStagesAsync(gaps, cancellationToken);

    public Task<int> AddVoteBillsAsync(int releaseNumber, IReadOnlyCollection<ReleaseVoteBill> voteBills, CancellationToken cancellationToken) =>
        inner.AddVoteBillsAsync(releaseNumber, voteBills, cancellationToken);

    public Task<IReadOnlyList<ReleaseVoteBill>> ReadVoteBillsAsync(int releaseNumber, CancellationToken cancellationToken) =>
        inner.ReadVoteBillsAsync(releaseNumber, cancellationToken);

    public Task<StoredRelease> ReadReleaseAsync(int releaseNumber, CancellationToken cancellationToken) => inner.ReadReleaseAsync(releaseNumber, cancellationToken);

    public Task<PublicationState> GetStateAsync(CancellationToken cancellationToken) => inner.GetStateAsync(cancellationToken);

    public Task<TimeSpan?> ReadAgeOfNewestReleaseAsync(CancellationToken cancellationToken) => inner.ReadAgeOfNewestReleaseAsync(cancellationToken);

    public Task<int> CountApprovedOutsideActiveReleaseAsync(IReadOnlyCollection<PublishedRecordReference> withdrawn, CancellationToken cancellationToken) =>
        inner.CountApprovedOutsideActiveReleaseAsync(withdrawn, cancellationToken);

    public Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken) => inner.ListAsync(limit, cancellationToken);

    public async Task<IReadOnlyList<PublicationReleaseSummary>> ListAllAsync(CancellationToken cancellationToken)
    {
        if (BeforeListingAll is not null) await BeforeListingAll();
        return await inner.ListAllAsync(cancellationToken);
    }

    public Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, Func<string, CancellationToken, Task> requireFiles,
        CancellationToken cancellationToken) => inner.ActivateAsync(releaseNumber, requireFiles, cancellationToken);

    public Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve, CancellationToken cancellationToken) =>
        inner.ServeActiveAsync(serve, cancellationToken);

    public Task PruneAsync(Func<ReleaseRetentionPlan, string?, CancellationToken, Task> retire, CancellationToken cancellationToken) =>
        inner.PruneAsync(retire, cancellationToken);

    public Task<IReadOnlyDictionary<PublishedRecordReference, ReviewedRecord>> ReadReviewStatusesAsync(
        IReadOnlyCollection<PublishedRevisionBinding> published, CancellationToken cancellationToken) =>
        inner.ReadReviewStatusesAsync(published, cancellationToken);

    public Task<IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset>> ReadFirstPublishedAsync(
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken) =>
        inner.ReadFirstPublishedAsync(records, cancellationToken);

    public Task<int> AddFirstPublishedAsync(IReadOnlyCollection<PublishedFirstPublication> records, CancellationToken cancellationToken) =>
        inner.AddFirstPublishedAsync(records, cancellationToken);

    public Task<IReadOnlyList<BillPublicationInput>> ReadBillInputsAsync(int? previousReleaseNumber, ImmutableArray<string> recordIds,
        CancellationToken cancellationToken) => inner.ReadBillInputsAsync(previousReleaseNumber, recordIds, cancellationToken);

    public Task PrepareBillHistoriesAsync(IReadOnlyCollection<BillHistoryCapture> histories, CancellationToken cancellationToken) =>
        inner.PrepareBillHistoriesAsync(histories, cancellationToken);

    public Task<IReadOnlyDictionary<PublishedRecordReference, StoredCorrectionHistory>> ReadCorrectionHistoriesAsync(int releaseNumber,
        IReadOnlyCollection<PublishedRevisionBinding> records, CancellationToken cancellationToken) =>
        inner.ReadCorrectionHistoriesAsync(releaseNumber, records, cancellationToken);

    public Task<IReadOnlyList<CorrectionHistoryBinding>> PrepareCorrectionHistoriesAsync(
        IReadOnlyCollection<PublicationCorrectionHistory> histories, CancellationToken cancellationToken) =>
        inner.PrepareCorrectionHistoriesAsync(histories, cancellationToken);

    public Task<CorrectionHistoryBootstrapPage> ReadCorrectionHistoryBootstrapPageAsync(CorrectionHistoryBootstrapCursor? after, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.ReadCorrectionHistoryBootstrapPageAsync(after, limit, readServedDirectory, cancellationToken);

    public Task<CorrectionHistoryVerificationPage> ReadCorrectionHistoryVerificationPageAsync(CorrectionHistoryVerificationCursor? after, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.ReadCorrectionHistoryVerificationPageAsync(after, limit, readServedDirectory, cancellationToken);

    public Task<IReadOnlyList<PublicationReleaseSummary>> ReadRetainedReleaseSelectionAsync(
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.ReadRetainedReleaseSelectionAsync(readServedDirectory, cancellationToken);

    public Task<string> ReadRetainedReleaseFingerprintAsync(Func<CancellationToken, Task<string?>> readServedDirectory,
        CancellationToken cancellationToken) => inner.ReadRetainedReleaseFingerprintAsync(readServedDirectory, cancellationToken);

    public Task<(int Added, int AlreadyPresent)> CommitCorrectionHistoryBootstrapAsync(PublicationReleaseSummary release,
        IReadOnlyCollection<CorrectionHistoryBinding> bindings, Func<CancellationToken, Task<string?>> readServedDirectory,
        CancellationToken cancellationToken) =>
        inner.CommitCorrectionHistoryBootstrapAsync(release, bindings, readServedDirectory, cancellationToken);

    public Task<BillCountReleasePage> ReadBillCountReleasePageAsync(int afterReleaseNumber, string? selectionFingerprint, int limit,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.ReadBillCountReleasePageAsync(afterReleaseNumber, selectionFingerprint, limit, readServedDirectory, cancellationToken);

    public Task<VotePartyTallyPage> ReadVotePartyTallyEvidenceAsync(VotePartyTallyVerificationCursor? after, int limit, CancellationToken cancellationToken) =>
        inner.ReadVotePartyTallyEvidenceAsync(after, limit, cancellationToken);

    public Task<ReleaseBillCountSource> ReadBillCountSourceAsync(int releaseNumber, CancellationToken cancellationToken) =>
        inner.ReadBillCountSourceAsync(releaseNumber, cancellationToken);

    public Task<ReleaseSnapshotWrite> CommitBillCountsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.CommitBillCountsAsync(release, validatedMembers, readServedDirectory, cancellationToken);

    public Task<BillCountStatus> VerifyBillCountsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.VerifyBillCountsAsync(release, validatedMembers, readServedDirectory, cancellationToken);

    public Task<ReleaseSnapshotWrite> CommitPartyLineUpsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.CommitPartyLineUpsAsync(release, validatedMembers, readServedDirectory, cancellationToken);

    public Task<BillCountStatus> VerifyPartyLineUpsAsync(PublicationReleaseSummary release, IReadOnlyCollection<PublishedRevisionBinding> validatedMembers,
        Func<CancellationToken, Task<string?>> readServedDirectory, CancellationToken cancellationToken) =>
        inner.VerifyPartyLineUpsAsync(release, validatedMembers, readServedDirectory, cancellationToken);

    public Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => inner.FindReplayAsync(actorSubject, idempotencyKey, payloadHash, cancellationToken);

    public Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => inner.CommitAsync(actorSubject, commit, idempotencyKey, payloadHash, cancellationToken);
}
