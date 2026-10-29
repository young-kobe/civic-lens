using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Core.ExecutiveActions;

/// <summary>One Federal Register presidential document as a record revision. Code writes every field. No person edits it.</summary>
public sealed record ExecutiveActionRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string SourceId, string DocumentNumber, string? CorrectionOf, ExecutiveActionKind Kind, string? PresidentialNumber, string Title,
    DateOnly SignedOn, DateOnly PublishedOn, string FederalRegisterCitation, string PresidentName, string PersonId, string DocumentUrl,
    ImmutableArray<ExecutiveActionFact> Facts)
    : IReviewRevision
{
    public const int MaximumFieldLength = 1_000;

    public IEnumerable<SourceSpan> Citations => Facts.Select(fact => fact.Citation);

    public ImmutableArray<string> OfficialIds() => [PersonId];

    public void ValidateShape()
    {
        if (!LowercaseHex.Is(RecordId, 64))
            throw new ArgumentException("An executive action revision needs a valid record ID.");
        if (!ReviewAuthor.IsValidRevisionHeader(RevisionNumber, AuthorSubject, CreatedAtUtc) || !ReviewAuthor.IsCode(AuthorSubject))
            throw new ArgumentException("An executive action revision needs a number, the code author, and a UTC creation time.");
        if (!Enum.IsDefined(Kind) || SignedOn == default || PublishedOn == default)
            throw new ArgumentException("An executive action revision needs a kind, a signing date, and a publication date.");
        foreach (var (_, value) in ExpectedFacts())
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumFieldLength || value.Contains('\0') || value.Contains('\n') || value.Contains('\r'))
                throw new ArgumentException($"A fact of an executive action revision has 1 to {MaximumFieldLength} characters on one line.");
        ReviewTextRules.ValidateText(SourceId, 128, nameof(SourceId));
        ReviewTextRules.ValidateText(PersonId, 128, nameof(PersonId));
        if (Facts.IsDefault || !Facts.Select(fact => (fact.Label, fact.Value)).SequenceEqual(ExpectedFacts()) || Facts.Any(fact => !IsBounded(fact.Citation)))
            throw new ArgumentException("An executive action revision needs its fact lines, each with a bounded citation.");
    }

    public void ValidateForApproval() => ValidateShape();

    public void ValidateForApproval(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ValidateForApproval();
        ValidateAgainst(extractions);
    }

    public void ValidateAgainst(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        foreach (var fact in Facts)
        {
            var span = new DocumentTextSpan(extractions.GetValueOrDefault(fact.Citation.ExtractionId)
                ?? throw new ArgumentException("An executive action citation names a missing extraction."), fact.Citation.Start, fact.Citation.Length);
            if (span.Quote != fact.Text)
                throw new ArgumentException("An executive action fact must equal the text of its citation.");
        }
    }

    private IReadOnlyList<(string Label, string Value)> ExpectedFacts() =>
        ExecutiveActionFacts.Of(DocumentNumber, CorrectionOf, Kind, PresidentialNumber, Title, SignedOn, PublishedOn, FederalRegisterCitation,
            PresidentName, DocumentUrl);

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);
}
