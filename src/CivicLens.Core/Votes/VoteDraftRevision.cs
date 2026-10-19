using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Core.Votes;

public sealed record VoteDraftRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, ReviewTextOrigin TextOrigin, ReviewTextSuggestion? Suggestion,
    string SourceId, int Congress, int SessionNumber, int RollCallNumber, string? BillType, int? BillNumber,
    string CandidateId, DateTimeOffset CandidateUpdateDate, ImmutableArray<VoteFact> Facts, DateOnly VotedOn, SourceSpan VotedOnCitation, VoteKind Kind, SourceSpan? KindCitation, ImmutableArray<MemberVote> Members,
    ImmutableArray<string> TrackedIssueIds, string IssueRevisionId)
    : IHeadlineSummaryRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 4_000;
    public const int MaximumFacts = 64;
    public const int MaximumFactLength = 2_000;
    public const int MaximumMembers = 600;

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    public IEnumerable<SourceSpan> Citations => new[] { VotedOnCitation }.Concat(Facts.Select(fact => fact.Citation))
        .Concat(KindCitation is { } kind ? [kind] : []).Concat(Members.Select(member => member.Citation));

    public ImmutableArray<string> OfficialIds() => [.. Members.Select(member => member.PersonId).Distinct(StringComparer.Ordinal)];

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64) || !LowercaseHex.Is(CandidateId, 64) || !LowercaseHex.Is(IssueRevisionId, 64))
            throw new ArgumentException("A vote revision needs valid record, candidate, and issue revision IDs.");
        if (RevisionNumber < 1 || string.IsNullOrWhiteSpace(SourceId) || Congress < 1 || SessionNumber is not (1 or 2) || RollCallNumber < 1)
            throw new ArgumentException("A vote revision needs a source and a roll call identity.");
        if ((BillType is null) != (BillNumber is null) || BillType is not null && (string.IsNullOrWhiteSpace(BillType) || BillNumber < 1))
            throw new ArgumentException("A bill type has a number, and a number has a type.");
        if (TrackedIssueIds.IsDefault || TrackedIssueIds.Length > StatementDraftRevision.MaximumSelections)
            throw new ArgumentException($"A vote revision needs at most {StatementDraftRevision.MaximumSelections} tracked issues.");
        ReviewTextRules.ValidateHeadlineAndSummary(this, MaximumHeadlineLength, MaximumSummaryLength);
        if (Facts.IsDefault || Facts.Length > MaximumFacts || Facts.Any(fact => !IsBounded(fact.Citation)))
            throw new ArgumentException($"A vote revision needs at most {MaximumFacts} facts with bounded citations.");
        foreach (var fact in Facts) ReviewTextRules.ValidateText(fact.Text, MaximumFactLength, nameof(Facts));
        if (VotedOn == default || !IsBounded(VotedOnCitation))
            throw new ArgumentException("A vote revision needs the vote day with a bounded citation of its start line.");
        if (!Enum.IsDefined(Kind) || KindCitation is { } kindCitation && !IsBounded(kindCitation) || Kind != VoteKind.Other && KindCitation is null)
            throw new ArgumentException("A vote kind other than Other needs a bounded citation of its question.");
        if (Members.IsDefaultOrEmpty || Members.Length > MaximumMembers)
            throw new ArgumentException($"A vote revision needs 1 to {MaximumMembers} member votes.");
        if (Members.Any(member => string.IsNullOrWhiteSpace(member.PersonId) || string.IsNullOrWhiteSpace(member.BioguideId) ||
                string.IsNullOrWhiteSpace(member.Cast) || !IsBounded(member.Citation)))
            throw new ArgumentException("Each member vote needs a person, a bioguide ID, a cast, and a bounded citation.");
    }

    public void ValidateForApproval() => ValidateShape();

    public void ValidateForApproval(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ValidateForApproval();
        ValidateAgainst(extractions);
    }

    public void ValidateAsAnalysisRevision(VoteDraftRevision? previous)
    {
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Analysis || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("An analysis revision follows the current revision and has the analysis author.");
        ReviewTextRules.RequireAnalysisText(this, previous);
    }

    public void ValidatePersonEditOf(VoteDraftRevision previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Person || RevisionNumber != previous.RevisionNumber + 1)
            throw new ArgumentException("A person revision follows the current revision.");
        if (!HasSameStructuredPartAs(previous))
            throw new ArgumentException("A person edits only the headline and the summary.");
        ReviewTextRules.RequireSuggestionKeptOrCleared(this, previous);
    }

    public void ValidateAgainst(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        foreach (var span in Citations)
            _ = new DocumentTextSpan(extractions.GetValueOrDefault(span.ExtractionId)
                ?? throw new ArgumentException("A vote citation names a missing extraction."), span.Start, span.Length);
    }

    private bool HasSameStructuredPartAs(VoteDraftRevision other) =>
        RecordId == other.RecordId && SourceId == other.SourceId && Congress == other.Congress && SessionNumber == other.SessionNumber &&
        RollCallNumber == other.RollCallNumber && BillType == other.BillType && BillNumber == other.BillNumber &&
        CandidateId == other.CandidateId && CandidateUpdateDate == other.CandidateUpdateDate && VotedOn == other.VotedOn &&
        VotedOnCitation == other.VotedOnCitation && Facts.SequenceEqual(other.Facts) &&
        Kind == other.Kind && KindCitation == other.KindCitation && Members.SequenceEqual(other.Members) &&
        TrackedIssueIds.SequenceEqual(other.TrackedIssueIds) && IssueRevisionId == other.IssueRevisionId;

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);
}
