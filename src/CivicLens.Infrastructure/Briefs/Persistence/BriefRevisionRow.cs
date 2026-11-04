using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Briefs.Persistence;

/// <summary>The columns after <see cref="CreatedAtUtcTicks"/> copy <see cref="RevisionJson"/>. They stay null on a row that
/// older code wrote.</summary>
internal sealed class BriefRevisionRow : IReviewRevisionRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
    public DateOnly? WeekStart { get; set; }
    public long? WindowStartUtcTicks { get; set; }
    public long? WindowEndUtcTicks { get; set; }
    public int? FirstReleaseNumber { get; set; }
    public int? CutoffReleaseNumber { get; set; }
    public string? Fingerprint { get; set; }
    public string? Headline { get; set; }
    public ReviewTextOrigin? TextOrigin { get; set; }
    public string? SuggestionHeadline { get; set; }
    public string? SuggestionRunId { get; set; }
    public string? SuggestionFingerprint { get; set; }
    public int? CoverageConsidered { get; set; }
    public int? CoverageSentToModel { get; set; }
    public List<BriefRevisionCitationRow> Citations { get; set; } = [];
    public List<BriefRevisionParagraphRow> Paragraphs { get; set; } = [];
}
