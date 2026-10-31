using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

/// <summary>A record that needs a person. A held reason exists only when code approval refused the newest revision and a later
/// revision or decision has not cleared it. An older record can hold none, so the reason then comes from the status.</summary>
public sealed record ReviewQueueItem(PublishedRecordKind Kind, string RecordId, string Headline, ReviewStatus Status, int OpenConcerns,
    string? HeldReason, DateTimeOffset LastRevisedAtUtc, bool EditedByPerson = false, RelationshipType? LinkType = null)
{
    public string Reason
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(HeldReason)) return HeldReason;
            return Status switch
            {
                ReviewStatus.AwaitingReviewWithConcerns => OpenConcerns == 1
                    ? "1 open concern needs an answer."
                    : $"{OpenConcerns} open concerns need an answer.",
                ReviewStatus.ChangesRequested => "A reviewer asked for changes. Revise the draft.",
                ReviewStatus.ApprovalWithdrawn => "A reviewer withdrew the approval. Review the draft again.",
                ReviewStatus.Approved => "This record is approved. Refresh the page.",
                _ when CodeApproval.CanApprove(Kind) && EditedByPerson => "A person edited this record. A person must approve it.",
                _ when CodeApproval.CanApprove(Kind) => "Code did not approve this record. A person must decide.",
                _ => "A person must review this draft."
            };
        }
    }
}
