using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Core.Actions;

public sealed record ActionDraftRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, ReviewTextOrigin TextOrigin, ReviewTextSuggestion? Suggestion,
    string SourceId, int Congress, string BillType, int BillNumber, string CandidateId, DateTimeOffset CandidateUpdateDate,
    bool CandidateIncludesSummaries,
    string Fingerprint, string? SummaryVersion, SourceSpan? SummaryCitation, string? PolicyArea, SourceSpan? PolicyAreaCitation,
    string? PolicyAreaId, ImmutableArray<string> TrackedIssueIds, string IssueRevisionId, ImmutableArray<ActionSponsorship> Sponsorships, string BillTitle)
    : IHeadlineSummaryRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSponsorships = 600;
    public const int MaximumTitleLength = 4_000;
    public const int MaximumCitationsPerSponsorship = 2;

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    public IEnumerable<SourceSpan> Citations => Sponsorships.SelectMany(item => item.Citations)
        .Concat(SummaryCitation is { } summary ? [summary] : []).Concat(PolicyAreaCitation is { } area ? [area] : []);

    public DateOnly EventOn() => Sponsorships.Max(item => item.WithdrawnOn ?? item.SponsoredOn);

    public ImmutableArray<string> OfficialIds() => [.. Sponsorships.Select(item => item.PersonId).Distinct(StringComparer.Ordinal)];

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64) || !LowercaseHex.Is(CandidateId, 64) || !LowercaseHex.Is(Fingerprint, 64) ||
            !LowercaseHex.Is(IssueRevisionId, 64))
            throw new ArgumentException("An action revision needs valid record, candidate, fingerprint, and issue revision IDs.");
        if (!ReviewAuthor.IsValidRevisionHeader(RevisionNumber, AuthorSubject, CreatedAtUtc))
            throw new ArgumentException("An action revision needs a number, an author, and a UTC creation time.");
        if (string.IsNullOrWhiteSpace(SourceId) || Congress < 1 || BillNumber < 1 || string.IsNullOrWhiteSpace(BillType))
            throw new ArgumentException("An action revision needs a source and a bill identity.");
        ReviewTextRules.ValidateHeadlineAndSummary(this, MaximumHeadlineLength, MaximumSummaryLength);
        ReviewTextRules.ValidateText(BillTitle, MaximumTitleLength, nameof(BillTitle));
        if (TrackedIssueIds.IsDefault || TrackedIssueIds.Length > StatementDraftRevision.MaximumSelections || Sponsorships.IsDefaultOrEmpty ||
            Sponsorships.Length > MaximumSponsorships)
            throw new ArgumentException("An action revision needs 1 to 600 sponsorships and at most 64 tracked issues.");
        if (Sponsorships.Any(item => item.Citations.IsDefaultOrEmpty || item.Citations.Length > MaximumCitationsPerSponsorship ||
                !item.Citations.All(IsBounded)))
            throw new ArgumentException("Each sponsorship needs bounded citations.");
        if (Sponsorships.Any(item => item.WithdrawnOn < item.SponsoredOn))
            throw new ArgumentException("A sponsorship cannot be withdrawn before it began.");
        if (SummaryCitation is { } summary && !IsBounded(summary) || PolicyAreaCitation is { } area && !IsBounded(area))
            throw new ArgumentException("Citations must be bounded spans.");
        if ((SummaryVersion is null) != (SummaryCitation is null) || (PolicyArea is null) != (PolicyAreaCitation is null))
            throw new ArgumentException("A summary version and a policy area each need their citation.");
        if (PolicyAreaId is not null && (PolicyArea is null || string.IsNullOrWhiteSpace(PolicyAreaId)))
            throw new ArgumentException("A policy area ID needs a policy area and text.");
    }

    public void ValidateAsAnalysisRevision(ActionDraftRevision? previous)
    {
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Analysis || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("An analysis revision follows the current revision and has the analysis author.");
        ReviewTextRules.RequireAnalysisText(this, previous);
    }

    public void ValidatePersonEditOf(ActionDraftRevision previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Person || RevisionNumber != previous.RevisionNumber + 1)
            throw new ArgumentException("A person revision follows the current revision.");
        if (!HasSameStructuredPartAs(previous))
            throw new ArgumentException("A person edits only the headline and the summary.");
        ReviewTextRules.RequireSuggestionKeptOrCleared(this, previous);
    }

    public void ValidateForApproval() => ValidateShape();

    public void ValidateForApproval(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ValidateForApproval();
        ValidateAgainst(extractions);
    }

    public void ValidateAgainst(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        foreach (var span in Citations)
            _ = new DocumentTextSpan(extractions.GetValueOrDefault(span.ExtractionId)
                ?? throw new ArgumentException("An action citation names a missing extraction."), span.Start, span.Length);
    }

    public bool HasSameSponsorsAs(IEnumerable<ActionSponsorship> other) => SponsorsOf(Sponsorships).SequenceEqual(SponsorsOf(other));

    private static IEnumerable<(string BioguideId, string PersonId, DateOnly SponsoredOn)> SponsorsOf(IEnumerable<ActionSponsorship> sponsorships) =>
        sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor).Select(item => (item.BioguideId, item.PersonId, item.SponsoredOn));

    private bool HasSameStructuredPartAs(ActionDraftRevision other) =>
        RecordId == other.RecordId && SourceId == other.SourceId && Congress == other.Congress && BillType == other.BillType &&
        BillNumber == other.BillNumber && CandidateId == other.CandidateId && CandidateUpdateDate == other.CandidateUpdateDate &&
        CandidateIncludesSummaries == other.CandidateIncludesSummaries &&
        Fingerprint == other.Fingerprint && SummaryCitation == other.SummaryCitation && PolicyAreaCitation == other.PolicyAreaCitation &&
        HasSameBillFactsAs(other) && Sponsorships.SequenceEqual(other.Sponsorships);

    private bool HasSameBillFactsAs(ActionDraftRevision other) =>
        SummaryVersion == other.SummaryVersion && PolicyArea == other.PolicyArea && PolicyAreaId == other.PolicyAreaId && IssueRevisionId == other.IssueRevisionId &&
        BillTitle == other.BillTitle && TrackedIssueIds.SequenceEqual(other.TrackedIssueIds);

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);
}
