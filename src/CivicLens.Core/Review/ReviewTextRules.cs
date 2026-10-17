namespace CivicLens.Core.Review;

public static class ReviewTextRules
{
    public static void ValidateText(string text, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Contains('\0'))
            throw new ArgumentException($"Text must be 1 to {maximumLength} characters and contain no NUL.", name);
    }

    public static void ValidateHeadlineAndSummary(IHeadlineSummaryRevision revision, int maximumHeadlineLength, int maximumSummaryLength)
    {
        ValidateText(revision.Headline, maximumHeadlineLength, nameof(revision.Headline));
        ValidateText(revision.Summary, maximumSummaryLength, nameof(revision.Summary));
        if (revision.Suggestion is { } suggestion)
        {
            ValidateText(suggestion.Headline, maximumHeadlineLength, nameof(revision.Suggestion));
            ValidateText(suggestion.Summary, maximumSummaryLength, nameof(revision.Suggestion));
            if (!LowercaseHex.Is(suggestion.RunId, 32) || !LowercaseHex.Is(suggestion.Fingerprint, 64))
                throw new ArgumentException("A suggestion needs its run ID and fingerprint.");
        }
        if (ReviewAuthor.KindOf(revision.AuthorSubject) == ReviewAuthorKind.Person && revision.TextOrigin != ReviewTextOrigin.Person)
            throw new ArgumentException("A person revision holds person text.");
    }

    /// <summary>An analysis revision with new text keeps person text and offers its own text as a suggestion.</summary>
    public static void RequireAnalysisText(IHeadlineSummaryRevision revision, IHeadlineSummaryRevision? previous)
    {
        if (previous is not null && HasSameText(revision, previous)) return;
        if (previous?.TextOrigin == ReviewTextOrigin.Person)
        {
            if (revision.TextOrigin != ReviewTextOrigin.Person || revision.Headline != previous.Headline ||
                revision.Summary != previous.Summary || revision.Suggestion is null)
                throw new ArgumentException("An analysis revision keeps the text that a person wrote and offers its own as a suggestion.");
        }
        else if (revision.TextOrigin != ReviewTextOrigin.Model || revision.Suggestion is not null)
            throw new ArgumentException("Model text has no suggestion.");
    }

    public static void RequireSuggestionKeptOrCleared(IHeadlineSummaryRevision revision, IHeadlineSummaryRevision previous)
    {
        if (revision.Suggestion is not null && revision.Suggestion != previous.Suggestion)
            throw new ArgumentException("A person may keep or clear the suggestion, but never change it.");
    }

    public static bool HasSameText(IHeadlineSummaryRevision revision, IHeadlineSummaryRevision other) =>
        revision.Headline == other.Headline && revision.Summary == other.Summary && revision.TextOrigin == other.TextOrigin &&
        revision.Suggestion == other.Suggestion;
}
