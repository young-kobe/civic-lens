using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Relationships;

internal sealed class FakeRelationshipReviewStore : IRelationshipReviewStore
{
    public Dictionary<string, PublishableRelationship> Publishable { get; } = [];
    public List<UnpublishedApprovedRelationship> Unpublished { get; } = [];
    public ImmutableArray<PublishedRevisionBinding> ReleaseRecordsAtLastRead { get; private set; }

    public Task<IReadOnlyList<UnpublishedApprovedRelationship>> ListUnpublishedApprovedAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnpublishedApprovedRelationship>>([.. Unpublished
            .Where(item => !excludedRecordIds.Contains(item.Relationship.RecordId)).Take(limit)]);

    public Task<IReadOnlySet<string>> FindCitingAsync(ImmutableArray<string> relationshipIds, ImmutableArray<PublishedRecordReference> cited,
        CancellationToken cancellationToken)
    {
        if (relationshipIds.Any(id => !Publishable.ContainsKey(id)))
            throw new InvalidDataException("A relationship in the release has no pair row, so its cited records are unknown.");
        IReadOnlySet<string> found = relationshipIds.Where(id =>
            new[] { Publishable[id].Review.CurrentRevision.Statement, Publishable[id].Review.CurrentRevision.Target }
                .Any(citation => cited.Contains(new(RelationshipRecordKinds.PublishedKindOf(citation.Kind), citation.RecordId)))).ToHashSet();
        return Task.FromResult(found);
    }

    public Task<IReadOnlyDictionary<string, PublishableRelationship>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken)
    {
        ReleaseRecordsAtLastRead = releaseRecords;
        IReadOnlyDictionary<string, PublishableRelationship> found = recordIds.Where(Publishable.ContainsKey)
            .ToDictionary(id => id, id => Publishable[id] with { CitedRecordsChanged = !HoldsBothSides(Publishable[id].Review.CurrentRevision, releaseRecords) });
        return Task.FromResult(found);
    }

    private static bool HoldsBothSides(RelationshipRevision revision, ImmutableArray<PublishedRevisionBinding> releaseRecords) =>
        releaseRecords.Contains(RelationshipRecordKinds.BindingOf(revision.Statement)) &&
        releaseRecords.Contains(RelationshipRecordKinds.BindingOf(revision.Target));

    public Task<RelationshipRevision?> GetRevisionAsync(string recordId, int revisionNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RelationshipRevision> SaveRevisionAsync(string actorSubject, RelationshipRevision revision, int expectedRevisionNumber,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RelationshipReviewRead?> GetAsync(string recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<RelationshipReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RelationshipReviewCounts> CountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
