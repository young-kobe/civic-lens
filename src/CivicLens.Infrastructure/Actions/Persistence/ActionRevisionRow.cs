using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Actions.Persistence;

/// <summary>The columns after <see cref="CreatedAtUtcTicks"/> copy <see cref="RevisionJson"/>. They stay null on a row that
/// older code wrote.</summary>
internal sealed class ActionRevisionRow : IReviewRevisionRow, IReviewTextRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public string? CodeVersion { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public ReviewTextOrigin? TextOrigin { get; set; }
    public string? SuggestionHeadline { get; set; }
    public string? SuggestionSummary { get; set; }
    public string? SuggestionRunId { get; set; }
    public string? SuggestionFingerprint { get; set; }
    public string? SourceId { get; set; }
    public int? Congress { get; set; }
    public string? BillType { get; set; }
    public int? BillNumber { get; set; }
    public string? CandidateId { get; set; }
    public long? CandidateUpdateUtcTicks { get; set; }
    public bool? CandidateIncludesSummaries { get; set; }
    public bool? CandidateIncludesSubjects { get; set; }
    public string? Fingerprint { get; set; }
    public string? SummaryVersion { get; set; }
    public string? SummaryCitationExtractionId { get; set; }
    public int? SummaryCitationStart { get; set; }
    public int? SummaryCitationLength { get; set; }
    public string? PolicyArea { get; set; }
    public string? PolicyAreaCitationExtractionId { get; set; }
    public int? PolicyAreaCitationStart { get; set; }
    public int? PolicyAreaCitationLength { get; set; }
    public string? PolicyAreaId { get; set; }
    public string? BillTitle { get; set; }
    public string? BillCitationExtractionId { get; set; }
    public int? BillCitationStart { get; set; }
    public int? BillCitationLength { get; set; }
    public string? IssueRevisionId { get; set; }
    public string? SubjectsHash { get; set; }
    public bool? IssueCandidatesRecorded { get; set; }
    public DateOnly? EventOn { get; set; }
    public int? CosponsorCount { get; set; }
    public string? TextPackageId { get; set; }
    public string? WhyItMatters { get; set; }
    public List<ActionRevisionIssueRow> Issues { get; set; } = [];
    public List<ActionRevisionSponsorshipRow> Sponsorships { get; set; } = [];
    public List<ActionRevisionTextCitationRow> TextCitations { get; set; } = [];
}
