using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionRow : IReviewRevisionRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }

    string IReviewRevisionRow.RecordId => CandidateId;
}
