using System.Collections.Immutable;
using System.Text.Json.Serialization;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Core.Votes;

namespace CivicLens.Core.Actions;

public sealed record ActionDraftRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, ReviewTextOrigin TextOrigin, ReviewTextSuggestion? Suggestion,
    string SourceId, int Congress, string BillType, int BillNumber, string CandidateId, DateTimeOffset CandidateUpdateDate,
    bool CandidateIncludesSummaries,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CandidateIncludesSubjects,
    string Fingerprint, string? SummaryVersion, SourceSpan? SummaryCitation, string? PolicyArea, SourceSpan? PolicyAreaCitation,
    string? PolicyAreaId, ImmutableArray<string> TrackedIssueIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> IssueCandidateIds, string IssueRevisionId, ImmutableArray<ActionSponsorship> Sponsorships, string BillTitle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubjectsHash = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SourceSpan? BillCitation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TextPackageId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WhyItMatters = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<ActionTextCitation> TextCitations = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<ActionKeyTerm> KeyTerms = default)
    : IHeadlineSummaryRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 8_000;
    public const int MaximumSponsorships = 600;
    public const int MaximumTitleLength = 4_000;
    public const int MaximumCitationsPerSponsorship = 2;
    public const int MaximumSourceIdLength = 128;
    public const int MaximumBillTypeLength = VoteDraftRevision.MaximumBillTypeLength;
    public const int MaximumPolicyAreaLength = 128;
    public const int MaximumPolicyAreaIdLength = 64;
    public const int MaximumSummaryVersionLength = 64;
    public const int MaximumPersonIdLength = VoteDraftRevision.MaximumPersonIdLength;
    public const int MaximumBioguideIdLength = VoteDraftRevision.MaximumBioguideIdLength;
    public const int MaximumIssueIdLength = VoteDraftRevision.MaximumIssueIdLength;
    public const int MaximumTextPackageIdLength = 64;
    public const int MaximumWhyItMattersSentences = 3;
    public const int MaximumTextCitations = QuotedSentenceLimits.MaximumSentences + MaximumWhyItMattersSentences;

    private ImmutableArray<ActionTextCitation> TextCitationList => TextCitations.IsDefault ? [] : TextCitations;

    [JsonIgnore]
    public ImmutableArray<ActionKeyTerm> KeyTermList => KeyTerms.IsDefault ? [] : KeyTerms;

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    public IEnumerable<SourceSpan> Citations => Sponsorships.SelectMany(item => item.Citations)
        .Concat(SummaryCitation is { } summary ? [summary] : []).Concat(PolicyAreaCitation is { } area ? [area] : [])
        .Concat(BillCitation is { } bill ? [bill] : []).Concat(TextCitationList.Select(citation => citation.Span)).Concat(KeyTermList.Select(term => term.Span));

    public DateOnly EventOn() => Sponsorships.Max(item => item.WithdrawnOn ?? item.SponsoredOn);

    public ImmutableArray<string> SponsorIds() =>
        [.. Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor).Select(item => item.PersonId).Distinct(StringComparer.Ordinal)];

    public ImmutableArray<string> CosponsorIds()
    {
        var sponsorIds = SponsorIds().ToHashSet(StringComparer.Ordinal);
        return [.. Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Cosponsor && item.WithdrawnOn is null && !sponsorIds.Contains(item.PersonId))
            .Select(item => item.PersonId).Distinct(StringComparer.Ordinal)];
    }

    public int CosponsorCount() => CosponsorIds().Length;

    public ImmutableArray<string> OfficialIds() => [.. Sponsorships.Select(item => item.PersonId).Distinct(StringComparer.Ordinal)];

    public ActionDraftRevision ComparableForm() =>
        this with { Headline = ComparableText.Lines(Headline), Summary = ComparableText.Lines(Summary) };

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64) || !LowercaseHex.Is(CandidateId, 64) || !LowercaseHex.Is(Fingerprint, 64) ||
            !LowercaseHex.Is(IssueRevisionId, 64))
            throw new ArgumentException("An action revision needs valid record, candidate, fingerprint, and issue revision IDs.");
        if (SubjectsHash is not null && !LowercaseHex.Is(SubjectsHash, 64))
            throw new ArgumentException("An action revision subjects hash must be 64 lowercase hex characters.");
        if (!ReviewAuthor.IsValidRevisionHeader(RevisionNumber, AuthorSubject, CreatedAtUtc))
            throw new ArgumentException("An action revision needs a number, an author, and a UTC creation time.");
        if (Congress < 1 || BillNumber < 1)
            throw new ArgumentException("An action revision needs a Congress and a bill number.");
        ReviewTextRules.ValidateText(SourceId, MaximumSourceIdLength, nameof(SourceId));
        ReviewTextRules.ValidateText(BillType, MaximumBillTypeLength, nameof(BillType));
        ReviewTextRules.ValidateHeadlineAndSummary(this, MaximumHeadlineLength, MaximumSummaryLength);
        ReviewTextRules.ValidateText(BillTitle, MaximumTitleLength, nameof(BillTitle));
        if (TrackedIssueIds.IsDefault || TrackedIssueIds.Length > StatementDraftRevision.MaximumSelections || Sponsorships.IsDefaultOrEmpty ||
            Sponsorships.Length > MaximumSponsorships)
            throw new ArgumentException("An action revision needs 1 to 600 sponsorships and at most 64 tracked issues.");
        ValidateIssueIds(TrackedIssueIds, nameof(TrackedIssueIds));
        if (!IssueCandidateIds.IsDefault) ValidateIssueIds(IssueCandidateIds, nameof(IssueCandidateIds));
        foreach (var item in Sponsorships)
        {
            ReviewTextRules.ValidateText(item.PersonId, MaximumPersonIdLength, nameof(ActionSponsorship.PersonId));
            ReviewTextRules.ValidateText(item.BioguideId, MaximumBioguideIdLength, nameof(ActionSponsorship.BioguideId));
        }
        if (!IssueCandidateIds.IsDefault && !TrackedIssueIds.All(IssueCandidateIds.Contains))
            throw new ArgumentException("The tracked issues of an action revision must come from its issue candidates.");
        if (Sponsorships.Any(item => item.Citations.IsDefaultOrEmpty || item.Citations.Length > MaximumCitationsPerSponsorship ||
                !item.Citations.All(IsBounded)))
            throw new ArgumentException("Each sponsorship needs bounded citations.");
        if (Sponsorships.Any(item => item.WithdrawnOn < item.SponsoredOn))
            throw new ArgumentException("A sponsorship cannot be withdrawn before it began.");
        if (SummaryCitation is { } summary && !IsBounded(summary) || PolicyAreaCitation is { } area && !IsBounded(area) ||
            BillCitation is { } bill && !IsBounded(bill))
            throw new ArgumentException("Citations must be bounded spans.");
        if (AuthorKind == ReviewAuthorKind.Code && BillCitation is null)
            throw new ArgumentException("Code text states the bill title and date, so the revision cites the bill page.");
        ValidateTextSource();
        if ((SummaryVersion is null) != (SummaryCitation is null) || (PolicyArea is null) != (PolicyAreaCitation is null))
            throw new ArgumentException("A summary version and a policy area each need their citation.");
        if (PolicyAreaId is not null && PolicyArea is null)
            throw new ArgumentException("A policy area ID needs a policy area and text.");
        if (SummaryVersion is not null) ReviewTextRules.ValidateText(SummaryVersion, MaximumSummaryVersionLength, nameof(SummaryVersion));
        if (PolicyArea is not null) ReviewTextRules.ValidateText(PolicyArea, MaximumPolicyAreaLength, nameof(PolicyArea));
        if (PolicyAreaId is not null) ReviewTextRules.ValidateText(PolicyAreaId, MaximumPolicyAreaIdLength, nameof(PolicyAreaId));
    }

    private void ValidateTextSource()
    {
        var citations = TextCitationList;
        if ((TextPackageId is null) != (WhyItMatters is null) || (TextPackageId is null) != citations.IsEmpty)
            throw new ArgumentException("A bill text package, a why it matters part, and text citations come together.");
        if (TextPackageId is null)
        {
            if (!KeyTermList.IsEmpty) throw new ArgumentException("Key terms come from the bill text, so a revision with no bill text has none.");
            return;
        }
        if (SummaryCitation is not null || SummaryVersion is not null)
            throw new ArgumentException("An action revision cites the CRS summary or the bill text, never both.");
        ReviewTextRules.ValidateText(TextPackageId, MaximumTextPackageIdLength, nameof(TextPackageId));
        ReviewTextRules.ValidateText(WhyItMatters!, MaximumSummaryLength, nameof(WhyItMatters));
        var summary = citations.Count(citation => citation.Part == ActionTextPart.Summary);
        var why = citations.Count(citation => citation.Part == ActionTextPart.WhyItMatters);
        var summaryComesFirst = citations.Take(summary).All(citation => citation.Part == ActionTextPart.Summary);
        if (summary is < QuotedSentenceLimits.MinimumSentences or > QuotedSentenceLimits.MaximumSentences || why is < 1 or > MaximumWhyItMattersSentences || !summaryComesFirst)
            throw new ArgumentException("Bill text gives 2 or 3 summary sentences, then 1 to 3 why it matters sentences.");
        if (citations.Any(citation => !IsBounded(citation.Span) || citation.Span.Length > QuotedSentenceLimits.MaximumQuoteLength))
            throw new ArgumentException("Each text citation is a bounded span of one quote.");
        if (citations.Any(citation => string.IsNullOrWhiteSpace(citation.Sentence) || citation.Sentence.Length > QuotedSentenceLimits.MaximumSentenceLength))
            throw new ArgumentException("Each text sentence has text and at most the maximum length.");
        ValidateKeyTerms();
    }

    private void ValidateKeyTerms()
    {
        var terms = KeyTermList;
        if (terms.Length > ActionKeyTerm.MaximumPerRevision)
            throw new ArgumentException($"A revision has at most {ActionKeyTerm.MaximumPerRevision} key terms.");
        if (terms.Any(item => string.IsNullOrWhiteSpace(item.Term) || item.Term.Length > ActionKeyTerm.MaximumTermLength ||
                string.IsNullOrWhiteSpace(item.Definition) || !IsBounded(item.Span) || item.Span.Length > QuotedSentenceLimits.MaximumQuoteLength ||
                item.Definition.Length > item.Span.Length || !item.IsDefinedBy(item.Definition)))
            throw new ArgumentException("Each key term has a term that its bounded definition quote holds.");
        if (terms.Select(item => item.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count() != terms.Length)
            throw new ArgumentException("A key term appears once in a revision.");
    }

    private static void ValidateIssueIds(ImmutableArray<string> ids, string name)
    {
        foreach (var id in ids) ReviewTextRules.ValidateText(id, MaximumIssueIdLength, name);
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("An issue ID appears once in a list.", name);
    }

    public void ValidateAsWorkerRevision(ActionDraftRevision? previous)
    {
        ValidateShape();
        if (AuthorKind == ReviewAuthorKind.Person || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("A worker revision follows the current revision and has the analysis or the code author.");
        if (AuthorKind == ReviewAuthorKind.Code) ReviewTextRules.RequireCodeText(this, previous);
        else ReviewTextRules.RequireAnalysisText(this, previous);
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
        if (TextPackageId is null) return;
        RequireTextSpansInTheBody(extractions);
        if (TextOrigin == ReviewTextOrigin.Model) RequireQuotesOfEnoughWords(extractions);
    }

    private void RequireTextSpansInTheBody(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        var extractionId = TextCitationList[0].Span.ExtractionId;
        var text = extractions[extractionId].Text;
        var bodyStart = BillTextBody.Start(text);
        if (bodyStart < 0 || TextCitationList.Select(citation => citation.Span).Concat(KeyTermList.Select(term => term.Span))
                .Any(span => span.ExtractionId != extractionId || span.Start < bodyStart))
            throw new ArgumentException("Each text citation lies in the body of one bill text extraction.");
        RequireKeyTermsInTheDefinitions(extractions[extractionId], bodyStart);
    }

    /// <summary>A key term defines itself by an exact quote of the definitions section. The section and the cut mark come from the same code rules that the draft step used.</summary>
    private void RequireKeyTermsInTheDefinitions(DocumentExtraction extraction, int bodyStart)
    {
        if (KeyTermList.IsEmpty) return;
        var body = extraction.Text[bodyStart..];
        var section = BillDefinitionsSection.Find(body);
        foreach (var term in KeyTermList)
        {
            if (section is null || !section.Contains(term.Span.Start - bodyStart, term.Span.Length))
                throw new ArgumentException("Each key term quotes the definitions section of the bill text.");
            if (term.Definition != GpoQuoteMarks.Normalize(new DocumentTextSpan(extraction, term.Span.Start, term.Span.Length).Quote))
                throw new ArgumentException("Each key term definition is the plain form of the exact quote that its citation names.");
            if (term.Cut != section.Cuts(body, term.Span.Start + term.Span.Length - bodyStart))
                throw new ArgumentException("Each key term cut mark agrees with the definition paragraph of its quote.");
        }
    }

    private void RequireQuotesOfEnoughWords(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ActionTextCitation[] Of(ActionTextPart part) => [.. TextCitationList.Where(citation => citation.Part == part)];
        if (Summary != ActionTextCitation.Join(Of(ActionTextPart.Summary)) || WhyItMatters != ActionTextCitation.Join(Of(ActionTextPart.WhyItMatters)))
            throw new ArgumentException("Model text drawn from the bill text is the join of its sentences.");
        foreach (var citation in TextCitationList)
        {
            if (ActionTextCitation.WordCount(citation.Span.QuoteIn(extractions)) < QuotedSentenceLimits.MinimumQuoteWords)
                throw new ArgumentException($"Each text quote has at least {QuotedSentenceLimits.MinimumQuoteWords} words.");
        }
    }

    public bool HasSameSponsorsAs(IEnumerable<ActionSponsorship> other) => SponsorsOf(Sponsorships).SequenceEqual(SponsorsOf(other));

    private static IEnumerable<(string BioguideId, string PersonId, DateOnly SponsoredOn)> SponsorsOf(IEnumerable<ActionSponsorship> sponsorships) =>
        sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor).Select(item => (item.BioguideId, item.PersonId, item.SponsoredOn));

    private bool HasSameStructuredPartAs(ActionDraftRevision other) =>
        RecordId == other.RecordId && SourceId == other.SourceId && Congress == other.Congress && BillType == other.BillType &&
        BillNumber == other.BillNumber && CandidateId == other.CandidateId && CandidateUpdateDate == other.CandidateUpdateDate &&
        CandidateIncludesSummaries == other.CandidateIncludesSummaries && CandidateIncludesSubjects == other.CandidateIncludesSubjects &&
        Fingerprint == other.Fingerprint && SummaryCitation == other.SummaryCitation && PolicyAreaCitation == other.PolicyAreaCitation &&
        BillCitation == other.BillCitation && TextPackageId == other.TextPackageId && WhyItMatters == other.WhyItMatters &&
        TextCitationList.SequenceEqual(other.TextCitationList) && KeyTermList.SequenceEqual(other.KeyTermList) &&
        HasSameBillFactsAs(other) && SubjectsHash == other.SubjectsHash && Sponsorships.SequenceEqual(other.Sponsorships);

    private bool HasSameBillFactsAs(ActionDraftRevision other) =>
        SummaryVersion == other.SummaryVersion && PolicyArea == other.PolicyArea && PolicyAreaId == other.PolicyAreaId && IssueRevisionId == other.IssueRevisionId &&
        BillTitle == other.BillTitle && TrackedIssueIds.SequenceEqual(other.TrackedIssueIds) &&
        IssueCandidateIds.IsDefault == other.IssueCandidateIds.IsDefault &&
        (IssueCandidateIds.IsDefault || IssueCandidateIds.SequenceEqual(other.IssueCandidateIds));

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);
}
