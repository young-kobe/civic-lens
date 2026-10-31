using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Briefs;

namespace CivicLens.Tests.Application.Briefs;

internal sealed class FakeBriefReviewStore : IBriefReviewStore
{
    public Dictionary<string, PublishableBrief> Publishable { get; } = [];
    public List<UnpublishedApprovedBrief> Unpublished { get; } = [];
    public ImmutableArray<PublishedRevisionBinding> ReleaseRecordsAtLastRead { get; private set; }
    public int PublicationReadCount { get; private set; }
    public int UnpublishedLimit { get; private set; }

    public Task<IReadOnlyList<UnpublishedApprovedBrief>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        UnpublishedLimit = limit;
        return Task.FromResult<IReadOnlyList<UnpublishedApprovedBrief>>([.. Unpublished
            .Where(item => !excludedRecordIds.Contains(item.Brief.RecordId)).Take(limit)]);
    }

    public Task<IReadOnlyDictionary<string, PublishableBrief>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken)
    {
        PublicationReadCount++;
        ReleaseRecordsAtLastRead = releaseRecords;
        IReadOnlyDictionary<string, PublishableBrief> found = recordIds.Where(Publishable.ContainsKey)
            .ToDictionary(id => id, id => FlagCitationsOutside(Publishable[id], releaseRecords));
        return Task.FromResult(found);
    }

    private static PublishableBrief FlagCitationsOutside(PublishableBrief brief, ImmutableArray<PublishedRevisionBinding> releaseRecords) =>
        brief with
        {
            Cited = brief.Review.CurrentRevision.Citations.ToImmutableDictionary(citation => citation.Id,
                citation => brief.Cited[citation.Id] with
                {
                    Changed = !releaseRecords.Contains(new(BriefCitationKinds.PublishedKindOf(citation.Kind), citation.RecordId,
                        citation.RevisionNumber))
                }, StringComparer.Ordinal)
        };

    public Task<BriefRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BriefRevision> SaveRevisionAsync(string actorSubject, BriefRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BriefReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

}
