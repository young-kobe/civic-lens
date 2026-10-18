using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Core.Statements;

public sealed record StatementDraftRevision(string CandidateId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, DateOnly? StatementDate, SourceSpan? StatementDateEvidence,
    ImmutableArray<string> SpeakerOfficialIds, ImmutableArray<string> OtherSpeakers, ImmutableArray<string> IssueIds,
    ImmutableArray<string> PolicyAreaIds, ImmutableArray<StatementQuotation> Quotations, ImmutableArray<string> BillNumbers) : IReviewRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSelections = 64;
    public const int MaximumOtherSpeakers = 16;
    public const int MaximumOtherSpeakerLength = 128;
    public const int MaximumQuotations = 32;
    public const int MaximumQuotationLength = 4_096;

    public static bool IsPlainSpeakerName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaximumOtherSpeakerLength && !name.Contains('\0') && name == name.Trim();

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    string IReviewRevision.RecordId => CandidateId;

    public DateOnly EventOn(DateOnly capturedOn) => StatementDate ?? capturedOn;

    public void ValidateAsFirstAnalysisRevision()
    {
        if (RevisionNumber != 1 || AuthorKind != ReviewAuthorKind.Analysis)
            throw new ArgumentException("An analysis statement draft must be revision 1 by the analysis author.");
        if ((StatementDate is null) != (StatementDateEvidence is null))
            throw new ArgumentException("A statement date requires an exact evidence span.");
        if (string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary) || Quotations.IsDefaultOrEmpty)
            throw new ArgumentException("A statement revision needs a headline, a summary, and a quotation.");
    }

    public StatementDraftRevision KeepingAiStatementDateOnlyIfChecked(StatementDraftRevision previous, bool statementDateChecked) =>
        AiProposedDate.IsUnconfirmedCarryOver(previous.AuthorSubject, previous.StatementDate, StatementDate, statementDateChecked)
            ? this with { StatementDate = null, StatementDateEvidence = null } : this;

    public void ValidateForApproval()
    {
        if (string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary) || SpeakerOfficialIds.IsDefaultOrEmpty ||
            Quotations.IsDefaultOrEmpty)
            throw new ArgumentException("Approval requires a headline, a summary, at least one speaker, and at least one quotation.");
        if ((StatementDate is null) != (StatementDateEvidence is null))
            throw new ArgumentException("A statement date requires an exact evidence span.");
        AiProposedDate.RequireConfirmed(AuthorSubject, StatementDate, "statement date");
    }

    public void ValidateShape()
    {
        ValidateText(Headline, MaximumHeadlineLength, nameof(Headline));
        ValidateText(Summary, MaximumSummaryLength, nameof(Summary));
        if ((StatementDate is null) != (StatementDateEvidence is null))
            throw new ArgumentException("A statement date requires an exact evidence span.");
        if (SpeakerOfficialIds.IsDefault || SpeakerOfficialIds.Length > MaximumSelections ||
            IssueIds.IsDefault || IssueIds.Length > MaximumSelections || PolicyAreaIds.IsDefault || PolicyAreaIds.Length > MaximumSelections)
            throw new ArgumentException($"A selection can hold at most {MaximumSelections} values.");
        if (OtherSpeakers.IsDefault || OtherSpeakers.Length > MaximumOtherSpeakers || !OtherSpeakers.All(IsPlainSpeakerName) ||
            OtherSpeakers.Distinct(StringComparer.Ordinal).Count() != OtherSpeakers.Length)
            throw new ArgumentException("Other speakers must be distinct plain names.");
        if (Quotations.IsDefault || Quotations.Length > MaximumQuotations || !Quotations.All(quotation => IsBoundedSpan(quotation.Span)) ||
            Quotations.Select(quotation => (quotation.Span.Start, quotation.Span.Length)).Distinct().Count() != Quotations.Length)
            throw new ArgumentException("Quotations must be distinct bounded spans.");
        if (StatementDateEvidence is not null && !IsBoundedSpan(StatementDateEvidence))
            throw new ArgumentException("The statement date evidence must be a bounded span.");
    }

    public void ValidateAgainst(DocumentExtraction extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        foreach (var span in Quotations.Select(quotation => quotation.Span).Concat(StatementDateEvidence is { } evidence ? [evidence] : []))
        {
            if (span.ExtractionId != extraction.ExtractionId)
                throw new ArgumentException("A statement can cite only the extraction of its candidate.");
            _ = new DocumentTextSpan(extraction, span.Start, span.Length);
        }
    }

    private static bool IsBoundedSpan(SourceSpan span) =>
        span is { Start: >= 0, Length: > 0 and <= MaximumQuotationLength };

    private static void ValidateText(string text, int maximumLength, string name)
    {
        if (text is null || text.Length > maximumLength || text.Contains('\0'))
            throw new ArgumentException($"Text must be at most {maximumLength} characters and contain no NUL.", name);
    }
}
