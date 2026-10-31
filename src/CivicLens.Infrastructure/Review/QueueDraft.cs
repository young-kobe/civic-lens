using CivicLens.Core.Relationships;
using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review;

internal sealed record QueueDraft(string Headline, ReviewStatus Status, int OpenConcerns, string? HeldReason, bool EditedByPerson,
    RelationshipType? LinkType = null, string? UnreadableError = null)
{
    public static QueueDraft Unreadable(string recordId, string error) =>
        new(recordId, ReviewStatus.NotDrafted, 0, null, false, UnreadableError: error);
}

internal sealed record QueueRead<TRevision>(TRevision Revision, ReviewStatus Status, int OpenConcerns, string? HeldReason)
    where TRevision : IReviewRevision
{
    public QueueDraft ToDraft(string headline, RelationshipType? linkType = null) =>
        new(headline, Status, OpenConcerns, HeldReason, HoldsPersonText, linkType);

    private bool HoldsPersonText =>
        !ReviewAuthor.IsReserved(Revision.AuthorSubject) || Revision is IHeadlineSummaryRevision { TextOrigin: ReviewTextOrigin.Person };
}
