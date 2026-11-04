using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionRow : IReviewRevisionRow, IHeadlineSummaryRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public DateOnly? StatementDate { get; set; }
    public string? StatementDateEvidenceExtractionId { get; set; }
    public int? StatementDateEvidenceStart { get; set; }
    public int? StatementDateEvidenceLength { get; set; }
    public List<StatementRevisionSpeakerRow> Speakers { get; set; } = [];
    public List<StatementRevisionOtherSpeakerRow> OtherSpeakers { get; set; } = [];
    public List<StatementRevisionIssueRow> Issues { get; set; } = [];
    public List<StatementRevisionPolicyAreaRow> PolicyAreas { get; set; } = [];
    public List<StatementRevisionQuotationRow> Quotations { get; set; } = [];
    public List<StatementRevisionBillNumberRow> BillNumbers { get; set; } = [];

    string IReviewRevisionRow.RecordId => CandidateId;
}
