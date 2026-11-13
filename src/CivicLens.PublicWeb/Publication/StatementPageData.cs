using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the statement page shows, read from one active release. The label is the label column of the public view. The source holds the saved text that every quote and the date evidence cite.
/// A time that the view does not hold is null, and the page shows no row for it.</summary>
public sealed record StatementPageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    DateOnly? StatementDate,
    string Headline,
    string Summary,
    PublishedTextOrigin TextOrigin,
    string? Label,
    PublishedApprover ApprovedBy,
    RecordOfficialData[] Speakers,
    RecordIssueData[] Issues,
    PublishedDocumentVersion Source,
    string? ArticleUrl,
    PublishedCitation? DateEvidence,
    PublishedQuotation[] Quotations,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? FirstPublishedAtUtc)
{
    public string ShortName => string.Join(", ", Speakers.Select(speaker => speaker.Name));

    /// <summary>The page the statement came from. A feed source lists an article, and the article is the original page.</summary>
    public string OriginalUrl => ArticleUrl ?? Source.Url;

    /// <summary>The host of the original page, such as kelly.senate.gov. A long URL is hard to read at the top of a page.</summary>
    public string OriginalHost => new Uri(OriginalUrl).Host;
}
