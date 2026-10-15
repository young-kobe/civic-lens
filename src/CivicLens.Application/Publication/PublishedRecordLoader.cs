using System.Collections.Immutable;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns one record kind for publication: its ID shape, its ready list, and how it loads and builds a record.</summary>
internal abstract class PublishedRecordLoader(PublishedRecordKind kind, string noun)
{
    public PublishedRecordKind Kind => kind;

    public abstract void ValidateId(string recordId);

    public abstract Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        CancellationToken cancellationToken);

    public abstract Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken);

    protected ArgumentException NotFound(string recordId) => new($"{noun} {recordId} does not exist.", "recordIds");

    protected ReadyToPublishRecord Ready(string recordId, string headline, int revisionNumber, int? publishedRevisionNumber) =>
        new(kind, recordId, headline, revisionNumber, publishedRevisionNumber);

    protected void RequirePublishable(string recordId, int revisionNumber, ImmutableArray<ReviewDecision> decisions,
        ImmutableArray<ReviewDecision> unresolvedConcerns, PreviousRelease previous)
    {
        var status = ReviewPolicy.GetCurrentStatus(revisionNumber, decisions, unresolvedConcerns);
        if (!PublishedRecordRules.IsPublishable(status, unresolvedConcerns.Length))
            throw new ArgumentException($"{noun} {recordId} is not approved or has unresolved concerns.");
        if (previous.Find(kind, recordId)?.RevisionNumber == revisionNumber)
            throw new ArgumentException($"{noun} {recordId} is already published at its current revision.");
    }
}
