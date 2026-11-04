using CivicLens.Core.Briefs;

namespace CivicLens.Infrastructure.Briefs.Persistence;

/// <summary>One citation of a brief revision. Exactly one pair of cited-revision columns is set, the pair of its kind, so that a foreign key proves the cited revision.</summary>
internal sealed class BriefRevisionCitationRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public BriefRevisionPart Part { get; set; }
    public string CitationId { get; set; } = string.Empty;
    public short Position { get; set; }
    public BriefCitationKind Kind { get; set; }
    public string? StatementCandidateId { get; set; }
    public int? StatementRevisionNumber { get; set; }
    public string? ActionRecordId { get; set; }
    public int? ActionRevisionNumber { get; set; }
    public string? DocumentChangeDraftId { get; set; }
    public int? DocumentChangeRevisionNumber { get; set; }
}
