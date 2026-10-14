namespace CivicLens.Core.Review;

public interface IReviewRevision
{
    string RecordId { get; }
    int RevisionNumber { get; }
    string AuthorSubject { get; }
    void ValidateShape();
    void ValidateForApproval();
}
