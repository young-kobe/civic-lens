using System.Collections.Immutable;

namespace CivicLens.Core.Review;

public sealed record DocumentChangeDraftRevision(string DraftId, int RevisionNumber, string ComparisonId,
    string AuthorSubject, DateTimeOffset CreatedAtUtc, string Headline, string Summary,
    string? Significance, string? Limits, string Institution,
    DateOnly? ChangeDate, DocumentChangeCitation? ChangeDateEvidence,
    ImmutableArray<string> OfficialIds, ImmutableArray<string> IssueIds,
    ImmutableArray<DocumentChangeCitation> Citations) : IReviewRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSignificanceLength = 4_000;
    public const int MaximumLimitsLength = 4_000;
    public const int MaximumInstitutionLength = 256;
    public const int MaximumCitations = 64;
    public const int MaximumCitationLength = 8_192;
    public const int MaximumSelections = 64;

    string IReviewRevision.RecordId => DraftId;

    public DocumentChangeDraftRevision KeepingAiChangeDateOnlyIfChecked(DocumentChangeDraftRevision previous, bool changeDateChecked) =>
        AiProposedDate.IsUnconfirmedCarryOver(previous.AuthorSubject, previous.ChangeDate, ChangeDate, changeDateChecked)
            ? this with { ChangeDate = null, ChangeDateEvidence = null } : this;

    public void ValidateForApproval()
    {
        if (string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary) ||
            string.IsNullOrWhiteSpace(Institution) || Citations.IsDefaultOrEmpty)
            throw new ArgumentException("Approval requires a headline, summary, source institution, and at least one citation.");
        if ((ChangeDate is null) != (ChangeDateEvidence is null))
            throw new ArgumentException("An actual change date requires an exact evidence citation.");
        AiProposedDate.RequireConfirmed(AuthorSubject, ChangeDate, "change date");
    }

    public static string ValidateDraftId(string value)
    {
        if (!LowercaseHex.Is(value, 32))
            throw new ArgumentException("Draft ID is invalid.", nameof(value));
        return value;
    }

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(DraftId, 32) || RevisionNumber < 1 || string.IsNullOrWhiteSpace(AuthorSubject) ||
            AuthorSubject.Length > 256 || CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Draft revision identity is invalid.");
        if (!LowercaseHex.Is(ComparisonId, 64))
            throw new ArgumentException("Identifier must be a lowercase SHA-256 value.", nameof(ComparisonId));
        ValidateText(Headline, MaximumHeadlineLength, nameof(Headline));
        ValidateText(Summary, MaximumSummaryLength, nameof(Summary));
        ValidateText(Significance, MaximumSignificanceLength, nameof(Significance));
        ValidateText(Limits, MaximumLimitsLength, nameof(Limits));
        ValidateText(Institution, MaximumInstitutionLength, nameof(Institution));
        if ((ChangeDate is null) != (ChangeDateEvidence is null) ||
            ChangeDateEvidence is not null && !Citations.Contains(ChangeDateEvidence))
            throw new ArgumentException("Change date must be bound to a citation included in the public evidence bindings.");
        ValidateSelections(OfficialIds, nameof(OfficialIds));
        ValidateSelections(IssueIds, nameof(IssueIds));
        if (Citations.IsDefault || Citations.Length > MaximumCitations)
            throw new ArgumentException($"Revision must contain at most {MaximumCitations} citations.");
        if (Citations.Any(citation => citation is null || citation.Start < 0 || citation.Length <= 0 || citation.Length > MaximumCitationLength) ||
            Citations.Distinct().Count() != Citations.Length)
            throw new ArgumentException("Citations must be distinct valid bounded spans.");
    }


    private static void ValidateText(string? value, int max, string name)
    {
        if (value is not null && (value.Length > max || value.Contains('\0')))
            throw new ArgumentException("Stored editorial text exceeds its bounds or contains NUL.", name);
    }

    private static void ValidateSelections(ImmutableArray<string> values, string name)
    {
        if (values.IsDefault || values.Length > MaximumSelections || values.Any(string.IsNullOrWhiteSpace) ||
            values.Any(value => value.Length > 128) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Stored editorial selections are invalid.", name);
    }
}
