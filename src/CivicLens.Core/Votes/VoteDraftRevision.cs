using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Core.Votes;

public sealed record VoteDraftRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string Headline, string Summary, string SourceId, int Congress, int SessionNumber, int RollCallNumber, string? BillType, int? BillNumber,
    string CandidateId, DateTimeOffset CandidateUpdateDate, ImmutableArray<VoteFact> Facts, ImmutableArray<MemberVote> Members)
    : IReviewRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumSummaryLength = 4_000;
    public const int MaximumFacts = 64;
    public const int MaximumFactLength = 2_000;
    public const int MaximumMembers = 600;

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64) || !LowercaseHex.Is(CandidateId, 64))
            throw new ArgumentException("A vote revision needs valid record and candidate IDs.");
        if (RevisionNumber < 1 || string.IsNullOrWhiteSpace(SourceId) || Congress < 1 || SessionNumber is not (1 or 2) || RollCallNumber < 1)
            throw new ArgumentException("A vote revision needs a source and a roll call identity.");
        if ((BillType is null) != (BillNumber is null) || BillType is not null && (string.IsNullOrWhiteSpace(BillType) || BillNumber < 1))
            throw new ArgumentException("A bill type has a number, and a number has a type.");
        ValidateText(Headline, MaximumHeadlineLength, nameof(Headline));
        ValidateText(Summary, MaximumSummaryLength, nameof(Summary));
        if (Facts.IsDefault || Facts.Length > MaximumFacts || Facts.Any(fact => !IsBounded(fact.Citation)))
            throw new ArgumentException($"A vote revision needs at most {MaximumFacts} facts with bounded citations.");
        foreach (var fact in Facts) ValidateText(fact.Text, MaximumFactLength, nameof(Facts));
        if (Members.IsDefaultOrEmpty || Members.Length > MaximumMembers)
            throw new ArgumentException($"A vote revision needs 1 to {MaximumMembers} member votes.");
        if (Members.Any(member => string.IsNullOrWhiteSpace(member.PersonId) || string.IsNullOrWhiteSpace(member.BioguideId) ||
                string.IsNullOrWhiteSpace(member.Cast) || !IsBounded(member.Citation)))
            throw new ArgumentException("Each member vote needs a person, a bioguide ID, a cast, and a bounded citation.");
    }

    public void ValidateForApproval() => ValidateShape();

    public void ValidateAsAnalysisRevision(VoteDraftRevision? previous)
    {
        ValidateShape();
        if (ReviewAuthor.KindOf(AuthorSubject) != ReviewAuthorKind.Analysis || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("An analysis revision follows the current revision and has the analysis author.");
    }

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);

    private static void ValidateText(string text, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Contains('\0'))
            throw new ArgumentException($"Text must be 1 to {maximumLength} characters and contain no NUL.", name);
    }
}
