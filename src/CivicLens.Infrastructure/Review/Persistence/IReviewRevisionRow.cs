namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IReviewRevisionRow
{
    string RecordId { get; }
    int RevisionNumber { get; }
    string RevisionJson { get; }
    long CreatedAtUtcTicks { get; }
}
