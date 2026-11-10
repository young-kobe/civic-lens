using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionRow : IReviewRevisionRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? CodeVersion { get; set; }
    public string? SourceId { get; set; }
    public string? DocumentNumber { get; set; }
    public ExecutiveActionKind? Kind { get; set; }
    public string? PresidentialNumber { get; set; }
    public string? Title { get; set; }
    public DateOnly? SignedOn { get; set; }
    public DateOnly? PublishedOn { get; set; }
    public string? FederalRegisterCitation { get; set; }
    public string? PresidentName { get; set; }
    public string? PersonId { get; set; }
    public string? DocumentUrl { get; set; }
    public string? DispositionNotes { get; set; }
    public string? DispositionNotesExtractionId { get; set; }
    public int? DispositionNotesStart { get; set; }
    public int? DispositionNotesLength { get; set; }
    public List<ExecutiveActionRevisionFactRow> Facts { get; set; } = [];
    public List<ExecutiveActionRevisionNoticeRow> Notices { get; set; } = [];
    public List<ExecutiveActionRevisionDispositionTargetRow> DispositionTargets { get; set; } = [];
}
