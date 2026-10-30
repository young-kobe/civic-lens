using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Core.ExecutiveActions;

/// <summary>The original Federal Register presidential document as a record revision, with the correction notices that name it. Code writes every field. No person edits it.</summary>
public sealed record ExecutiveActionRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    string SourceId, string DocumentNumber, ExecutiveActionKind Kind, string? PresidentialNumber, string Title,
    DateOnly SignedOn, DateOnly PublishedOn, string FederalRegisterCitation, string PresidentName, string PersonId, string DocumentUrl,
    ImmutableArray<ExecutiveActionFact> Facts, ImmutableArray<ExecutiveActionNotice> Notices)
    : IReviewRevision
{
    public const int MaximumFieldLength = 1_000;
    public const int MaximumNotices = 8;

    public IEnumerable<SourceSpan> Citations => Facts.Select(fact => fact.Citation).Concat(Notices.Select(notice => notice.Citation));

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
        if (Notices.IsDefault || Notices.Length > MaximumNotices || Notices.Any(notice => !HasBoundedFields(notice) || !IsBounded(notice.Citation)) ||
            Notices.Select(notice => notice.DocumentNumber).Distinct(StringComparer.Ordinal).Count() != Notices.Length || Notices.Any(notice => notice.DocumentNumber == DocumentNumber))
            throw new ArgumentException($"An executive action revision needs at most {MaximumNotices} distinct correction notices of other documents, each with a bounded citation.");
    }

    public void ValidateForApproval() => ValidateShape();

    public void ValidateForApproval(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ValidateForApproval();
        ValidateAgainst(extractions);
    }

    public void ValidateAgainst(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        foreach (var fact in Facts) RequireQuote(extractions, fact.Citation, fact.Text);
        foreach (var notice in Notices) RequireQuote(extractions, notice.Citation, notice.Text);
    }

    private static void RequireQuote(IReadOnlyDictionary<string, DocumentExtraction> extractions, SourceSpan citation, string text)
    {
        var span = new DocumentTextSpan(extractions.GetValueOrDefault(citation.ExtractionId)
            ?? throw new ArgumentException("An executive action citation names a missing extraction."), citation.Start, citation.Length);
        if (span.Quote != text)
            throw new ArgumentException("An executive action fact must equal the text of its citation.");
    }

    private static bool HasBoundedFields(ExecutiveActionNotice notice) =>
        !string.IsNullOrWhiteSpace(notice.DocumentNumber) && notice.DocumentNumber.Length <= 32 && !string.IsNullOrWhiteSpace(notice.Url) &&
        notice.Url.Length <= MaximumFieldLength && notice.PublishedOn != default;

    private IReadOnlyList<(string Label, string Value)> ExpectedFacts() =>
        ExecutiveActionFacts.Of(DocumentNumber, Kind, PresidentialNumber, Title, SignedOn, PublishedOn, FederalRegisterCitation,
            PresidentName, DocumentUrl);

    private static bool IsBounded(SourceSpan span) => span is { Start: >= 0, Length: > 0 } && LowercaseHex.Is(span.ExtractionId, 64);
}
