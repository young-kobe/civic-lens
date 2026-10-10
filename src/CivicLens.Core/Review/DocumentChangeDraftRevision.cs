using System.Collections.Immutable;

namespace CivicLens.Core.Review;

public sealed record DocumentChangeDraftRevision(string DraftId, int RevisionNumber, string ComparisonId,
    string AuthorSubject, DateTimeOffset CreatedAtUtc, string Headline, string Summary,
    string? Significance, string? Limits, string Institution,
    DateOnly? ChangeDate, DocumentChangeCitation? ChangeDateEvidence,
    ImmutableArray<string> OfficialIds, ImmutableArray<string> IssueIds,
    ImmutableArray<DocumentChangeCitation> Citations)
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSignificanceLength = 4_000;
    public const int MaximumLimitsLength = 4_000;
    public const int MaximumInstitutionLength = 256;
    public const int MaximumCitations = 64;
    public const int MaximumCitationLength = 8_192;
    public const int MaximumSelections = 64;

    public DocumentChangeDraftRevision KeepingAiChangeDateOnlyIfChecked(DocumentChangeDraftRevision previous, bool changeDateChecked) =>
        ReviewAuthor.IsAnalysis(previous.AuthorSubject) && previous.ChangeDate is not null && ChangeDate == previous.ChangeDate &&
        !changeDateChecked ? this with { ChangeDate = null, ChangeDateEvidence = null } : this;

    public void ValidateForApproval()
    {
        if (string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary) ||
            string.IsNullOrWhiteSpace(Institution) || Citations.IsDefaultOrEmpty)
            throw new ArgumentException("Approval requires a headline, summary, source institution, and at least one citation.");
        if ((ChangeDate is null) != (ChangeDateEvidence is null))
            throw new ArgumentException("An actual change date requires an exact evidence citation.");
        if (ReviewAuthor.IsAnalysis(AuthorSubject) && ChangeDate is not null)
            throw new ArgumentException("A person must confirm the AI-proposed change date by saving a revision before approval.");
    }
}
