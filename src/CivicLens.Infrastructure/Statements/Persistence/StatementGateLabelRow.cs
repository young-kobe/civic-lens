using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementGateLabelRow
{
    public string LabelId { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public string ExtractionId { get; set; } = string.Empty;
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public string LabelJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
}
