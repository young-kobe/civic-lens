using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CivicLens.Core.Review;

namespace CivicLens.Core.Briefs;

public sealed partial record BriefRevision(string RecordId, int RevisionNumber, string AuthorSubject, DateTimeOffset CreatedAtUtc,
    BriefWindow Window, string Fingerprint, string Headline, ReviewTextOrigin TextOrigin, BriefTextSuggestion? Suggestion,
    ImmutableArray<BriefParagraph> Paragraphs, ImmutableArray<BriefCitation> Citations, BriefCoverage Coverage) : IReviewRevision
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumParagraphLength = 4_000;
    public const int MaximumParagraphs = 12;
    public const int MaximumCitations = 256;
    public const int MaximumRecordIdLength = 64;

    public ReviewAuthorKind AuthorKind => ReviewAuthor.KindOf(AuthorSubject);

    public void ValidateShape()
    {
        ValidateIdentity();
        ValidateContent(Headline, Paragraphs, Citations, nameof(Headline));
        if (Coverage is null || Coverage.Cited != Citations.Length || Coverage.Considered < Coverage.SentToModel)
            throw new ArgumentException("The coverage must count the listed citations, and it cannot send more records than it considered.");
        if (Suggestion is { } suggestion)
        {
            ValidateContent(suggestion.Headline, suggestion.Paragraphs, suggestion.Citations, nameof(Suggestion));
            if (!LowercaseHex.Is(suggestion.RunId, 32) || !LowercaseHex.Is(suggestion.Fingerprint, 64))
                throw new ArgumentException("A suggestion needs its run ID and fingerprint.");
        }
        if (AuthorKind == ReviewAuthorKind.Person && TextOrigin != ReviewTextOrigin.Person)
            throw new ArgumentException("A person revision holds person text.");
    }

    public void ValidateAsAnalysisRevision(BriefRevision? previous)
    {
        ValidateShape();
        if (AuthorKind != ReviewAuthorKind.Analysis || RevisionNumber != (previous?.RevisionNumber ?? 0) + 1)
            throw new ArgumentException("An analysis revision follows the current revision and has the analysis author.");
        if (previous?.TextOrigin == ReviewTextOrigin.Person)
        {
            if (TextOrigin != ReviewTextOrigin.Person || Headline != previous.Headline || !Paragraphs.SequenceEqual(previous.Paragraphs) ||
                !Citations.SequenceEqual(previous.Citations) || Suggestion is null)
                throw new ArgumentException("An analysis revision keeps the text that a person wrote and offers its own as a suggestion.");
        }
        else if (TextOrigin != ReviewTextOrigin.Model || Suggestion is not null)
            throw new ArgumentException("Model text has no suggestion.");
    }

    public void ValidateForApproval() => ValidateShape();

    private void ValidateIdentity()
    {
        if (!LowercaseHex.Is(RecordId, 64) || !LowercaseHex.Is(Fingerprint, 64))
            throw new ArgumentException("A brief revision needs a valid record ID and fingerprint.");
        if (RevisionNumber < 1 || string.IsNullOrWhiteSpace(AuthorSubject) || AuthorSubject.Length > 256 || CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A brief revision needs a number, an author, and a UTC creation time.");
        Window.Validate();
        if (RecordId != Window.RecordId)
            throw new ArgumentException("A brief record ID must name its week.");
    }

    private static void ValidateContent(string headline, ImmutableArray<BriefParagraph> paragraphs, ImmutableArray<BriefCitation> citations,
        string name)
    {
        ValidateText(headline, MaximumHeadlineLength, name);
        ValidateCitations(citations, name);
        if (paragraphs.IsDefaultOrEmpty || paragraphs.Length > MaximumParagraphs)
            throw new ArgumentException($"A brief needs 1 to {MaximumParagraphs} paragraphs.", name);
        var known = citations.Select(citation => citation.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var paragraph in paragraphs)
        {
            ValidateText(paragraph.Text, MaximumParagraphLength, name);
            if (paragraph.CitationIds.IsDefaultOrEmpty || paragraph.CitationIds.Distinct(StringComparer.Ordinal).Count() != paragraph.CitationIds.Length)
                throw new ArgumentException("Each paragraph cites 1 or more distinct IDs.", name);
            if (!paragraph.CitationIds.All(known.Contains))
                throw new ArgumentException("A paragraph cites an ID that the brief does not list.", name);
        }
        var cited = paragraphs.SelectMany(paragraph => paragraph.CitationIds).ToHashSet(StringComparer.Ordinal);
        if (!known.All(cited.Contains))
            throw new ArgumentException("Every listed citation is cited in a paragraph.", name);
    }

    private static void ValidateCitations(ImmutableArray<BriefCitation> citations, string name)
    {
        if (citations.IsDefault || citations.Length is < 1 or > MaximumCitations)
            throw new ArgumentException($"A brief needs 1 to {MaximumCitations} citations.", name);
        if (citations.Any(citation => !CitationId().IsMatch(citation.Id) || !Enum.IsDefined(citation.Kind) ||
                string.IsNullOrWhiteSpace(citation.RecordId) || citation.RecordId.Length > MaximumRecordIdLength || citation.RevisionNumber < 1))
            throw new ArgumentException("A brief citation needs an ID, a kind, a record, and a revision.", name);
        if (citations.Select(citation => citation.Id).Distinct(StringComparer.Ordinal).Count() != citations.Length ||
            citations.Select(citation => (citation.Kind, citation.RecordId)).Distinct().Count() != citations.Length)
            throw new ArgumentException("A brief cites each record once, under its own ID.", name);
    }

    private static void ValidateText(string text, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Contains('\0'))
            throw new ArgumentException($"Text must be 1 to {maximumLength} characters and contain no NUL.", name);
    }

    [GeneratedRegex(@"^R[1-9][0-9]{0,3}$")]
    private static partial Regex CitationId();
}
