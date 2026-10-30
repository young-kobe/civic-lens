using System.Collections.Immutable;
using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class RelationshipRecordLoader(IRelationshipReviewStore relationships)
    : PublishedRecordLoader<UnpublishedApprovedRelationship, PublishableRelationship>(PublishedRecordKind.Relationship, "Relationship")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedRelationship>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        relationships.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedRelationship candidate) =>
        candidate.CitedRecordsChanged
            ? null
            : Ready(candidate.Relationship.CurrentStatus, candidate.Relationship.UnresolvedConcerns.Length, candidate.Relationship.RecordId,
                PublishedRelationshipBuilder.HeadlineOf(candidate.StatementHeadline, candidate.TargetHeadline),
                candidate.Relationship.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber)
                is { } ready ? ready with { NoLongerMatches = candidate.Relationship.NoLongerMatches } : null;

    protected override Task<IReadOnlyDictionary<string, PublishableRelationship>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        relationships.GetForPublicationAsync(recordIds, build.ReleaseRecords, cancellationToken);

    public async Task<IReadOnlySet<string>> FindCitingAsync(ImmutableArray<string> relationshipIds,
        IReadOnlyCollection<PublishedRecordReference> cited, CancellationToken cancellationToken) =>
        relationshipIds.IsEmpty ? new HashSet<string>() : await relationships.FindCitingAsync(relationshipIds, [.. cited], cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableRelationship addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        if (addition.CitedRecordsChanged)
            throw new ArgumentException(
                $"Relationship {recordId} cites a record that the new release does not hold at the cited revision, or that is not approved.");
        var record = new PublishedRelationshipBuilder(build.Rules).Build(addition, build.Previous.FirstPublishedAt(Kind, recordId), build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (_, cancellationToken) => build.Renderer.RenderRelationshipAsync(record, build.Catalog, cancellationToken));
    }
}
