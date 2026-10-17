namespace CivicLens.Core.Review;

public readonly record struct ReviewText(string Headline, string Summary, ReviewTextOrigin Origin, ReviewTextSuggestion? Suggestion)
{
    public static ReviewText CopyOf(IHeadlineSummaryRevision revision) =>
        new(revision.Headline, revision.Summary, revision.TextOrigin, revision.Suggestion);

    /// <summary>Person text stays as it is. The model text becomes a suggestion.</summary>
    public static ReviewText AfterModelDraft(IHeadlineSummaryRevision? current, string headline, string summary, string runId,
        string fingerprint) =>
        current is { TextOrigin: ReviewTextOrigin.Person }
            ? new(current.Headline, current.Summary, ReviewTextOrigin.Person, new(headline, summary, runId, fingerprint))
            : new(headline, summary, ReviewTextOrigin.Model, null);
}
