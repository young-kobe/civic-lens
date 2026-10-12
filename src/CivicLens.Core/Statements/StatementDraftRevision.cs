using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Statements;

public sealed record StatementDraftRevision(string CandidateId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, DateOnly? StatementDate, StatementSpan? StatementDateEvidence,
    ImmutableArray<string> SpeakerOfficialIds, ImmutableArray<string> OtherSpeakers, ImmutableArray<string> IssueIds,
    ImmutableArray<string> PolicyAreaIds, ImmutableArray<StatementQuotation> Quotations, ImmutableArray<string> BillNumbers)
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSelections = 64;
    public const int MaximumOtherSpeakers = 16;
    public const int MaximumOtherSpeakerLength = 128;
    public const int MaximumQuotations = 32;
    public const int MaximumQuotationLength = 4_096;

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    public void ValidateAsFirstAnalysisRevision()
    {
        if (RevisionNumber != 1 || AuthorKind != ReviewAuthorKind.Analysis)
            throw new ArgumentException("An analysis statement draft must be revision 1 by the analysis author.");
        if ((StatementDate is null) != (StatementDateEvidence is null))
            throw new ArgumentException("A statement date requires an exact evidence span.");
        if (string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary) || Quotations.IsDefaultOrEmpty)
            throw new ArgumentException("A statement revision needs a headline, a summary, and a quotation.");
    }
}
