using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionRow : IReviewRevisionRow, IHeadlineSummaryRow
{
    public string DraftId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? ComparisonId { get; set; }
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public string? Significance { get; set; }
    public string? Limits { get; set; }
    public string? Institution { get; set; }
    public DateOnly? ChangeDate { get; set; }
    public List<DocumentChangeRevisionOfficialRow> Officials { get; set; } = [];
    public List<DocumentChangeRevisionIssueRow> Issues { get; set; } = [];
    public List<DocumentChangeRevisionCitationRow> Citations { get; set; } = [];

    string IReviewRevisionRow.RecordId => DraftId;
}
