using System.Collections.Immutable;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Core.Actions;

public sealed record ActionDraftRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, ActionTextOrigin TextOrigin, ActionTextSuggestion? Suggestion,
    string SourceId, int Congress, string BillType, int BillNumber, string CandidateId, DateTimeOffset CandidateUpdateDate,
    bool CandidateIncludesSummaries,
    string Fingerprint, string? SummaryVersion, StatementSpan? SummaryCitation, string? PolicyArea, StatementSpan? PolicyAreaCitation,
    ImmutableArray<string> TrackedIssueIds, string IssueRevisionId, ImmutableArray<ActionSponsorship> Sponsorships, string BillTitle)
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSponsorships = 600;
    public const int MaximumTitleLength = 4_000;
    public const int MaximumCitationsPerSponsorship = 2;

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64) || !LowercaseHex.Is(CandidateId, 64) || !LowercaseHex.Is(Fingerprint, 64) ||
            !LowercaseHex.Is(IssueRevisionId, 64))
            throw new ArgumentException("An action revision needs valid record, candidate, fingerprint, and issue revision IDs.");
        if (RevisionNumber < 1 || string.IsNullOrWhiteSpace(SourceId) || Congress < 1 || BillNumber < 1 || string.IsNullOrWhiteSpace(BillType))
            throw new ArgumentException("An action revision needs a source and a bill identity.");
        ValidateText(Headline, MaximumHeadlineLength, nameof(Headline));
        ValidateText(Summary, MaximumSummaryLength, nameof(Summary));
        ValidateText(BillTitle, MaximumTitleLength, nameof(BillTitle));
        if (Suggestion is { } suggestion)
        {
            ValidateText(suggestion.Headline, MaximumHeadlineLength, nameof(Suggestion));
            ValidateText(suggestion.Summary, MaximumSummaryLength, nameof(Suggestion));
            if (!LowercaseHex.Is(suggestion.RunId, 32) || !LowercaseHex.Is(suggestion.Fingerprint, 64))
                throw new ArgumentException("A suggestion needs its run ID and fingerprint.");
        }
        if (TrackedIssueIds.IsDefault || TrackedIssueIds.Length > StatementDraftRevision.MaximumSelections || Sponsorships.IsDefaultOrEmpty ||
            Sponsorships.Length > MaximumSponsorships)
            throw new ArgumentException("An action revision needs 1 to 600 sponsorships and at most 64 tracked issues.");
        if (Sponsorships.Any(item => item.Citations.IsDefaultOrEmpty || item.Citations.Length > MaximumCitationsPerSponsorship ||
                !item.Citations.All(IsBounded)))
            throw new ArgumentException("Each sponsorship needs bounded citations.");
        if (SummaryCitation is { } summary && !IsBounded(summary) || PolicyAreaCitation is { } area && !IsBounded(area))
            throw new ArgumentException("Citations must be bounded spans.");
        if ((SummaryVersion is null) != (SummaryCitation is null) || (PolicyArea is null) != (PolicyAreaCitation is null))
            throw new ArgumentException("A summary version and a policy area each need their citation.");
        if (AuthorKind == ReviewAuthorKind.Person && TextOrigin != ActionTextOrigin.Person)
            throw new ArgumentException("A person revision holds person text.");
    }

    public void ValidateAsAnalysisRevision(ActionDraftRevision? previous)
    {
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Analysis || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("An analysis revision follows the current revision and has the analysis author.");
        if (previous?.TextOrigin == ActionTextOrigin.Person)
        {
            if (TextOrigin != ActionTextOrigin.Person || Headline != previous.Headline || Summary != previous.Summary || Suggestion is null)
                throw new ArgumentException("An analysis revision keeps the text that a person wrote and offers its own as a suggestion.");
        }
        else if (TextOrigin != ActionTextOrigin.Model || Suggestion is not null)
            throw new ArgumentException("Model text has no suggestion.");
    }

    public void ValidatePersonEditOf(ActionDraftRevision previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Person || RevisionNumber != previous.RevisionNumber + 1)
            throw new ArgumentException("A person revision follows the current revision.");
        if (!HasSameStructuredPartAs(previous))
            throw new ArgumentException("A person edits only the headline and the summary.");
    }

    public bool HasSameStructuredPartAs(ActionDraftRevision other) =>
        RecordId == other.RecordId && SourceId == other.SourceId && Congress == other.Congress && BillType == other.BillType &&
        BillNumber == other.BillNumber && CandidateId == other.CandidateId && CandidateUpdateDate == other.CandidateUpdateDate &&
        CandidateIncludesSummaries == other.CandidateIncludesSummaries &&
        Fingerprint == other.Fingerprint && SummaryVersion == other.SummaryVersion && SummaryCitation == other.SummaryCitation &&
        PolicyArea == other.PolicyArea && PolicyAreaCitation == other.PolicyAreaCitation && IssueRevisionId == other.IssueRevisionId && BillTitle == other.BillTitle &&
        TrackedIssueIds.SequenceEqual(other.TrackedIssueIds) && Sponsorships.SequenceEqual(other.Sponsorships) &&
        Suggestion == other.Suggestion;

    private static bool IsBounded(StatementSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);

    private static void ValidateText(string text, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Contains('\0'))
            throw new ArgumentException($"Text must be 1 to {maximumLength} characters and contain no NUL.", name);
    }
}
