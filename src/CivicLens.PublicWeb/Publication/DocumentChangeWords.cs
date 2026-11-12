using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words, anchors, and facts of the document change page.</summary>
public static class DocumentChangeWords
{
    public const int ShownChanges = 10;

    public const string ChangeDateAnchor = "cite-change-date";

    public const string EarlierAnchor = "earlier-version";

    public const string LaterAnchor = "later-version";

    public const string CheckLine = "Code found the changed words.";

    public const string ModelCheckLine = CheckLine + " A model wrote only this summary.";

    public const string Limit = "A change in wording does not show why the source changed.";

    public const string WhenHeading = "When it changed";

    public const string ChangedHeading = "What changed";

    public const string ChangedNote = "Struck text was removed. Underlined text was added. Words between the marks are unchanged.";

    public const string VersionsHeading = "Both versions";

    public const string WhyItMatters = "Why it matters";

    public const string EvidenceLimits = "What the evidence cannot establish";

    public static string Eyebrow(DocumentChangePageData data) => $"Document change | {data.Institution}";

    public static string Check(PublishedTextOrigin origin) =>
        $"{(origin == PublishedTextOrigin.Model ? ModelCheckLine : CheckLine)} {Limit}";

    public static string Lede(DocumentChangePageData data) =>
        $"Civic Lens saved this page twice. The text changed in {data.Changes.Length} {(data.Changes.Length == 1 ? "place" : "places")} between the two saves.";

    /// <summary>The time frame of the change. The two checks bound it. A change date that the source states comes first.</summary>
    public static string When(DocumentChangePageData data)
    {
        var checks = $"our check on {Moment.Full(data.Before.ObservedAtUtc)} and our check on {Moment.Full(data.After.ObservedAtUtc)}";
        return data.ChangeDate is { } date
            ? $"The source gives the change date as {Moment.Day(date)}. The page was different between {checks}."
            : $"The page was different between {checks}. We do not know the day it changed.";
    }

    public static string PassageLabel(int index, int count) => $"Passage {index + 1} of {count}";

    public static string ShownNote(int count) =>
        $"{count} passages changed. The first {ShownChanges} are shown. Both full texts are in the source fold.";

    public static string SavedLine(PublishedDocumentVersion version) => $"Saved {Moment.Full(version.ObservedAtUtc)}";

    /// <summary>The version that a citation quotes. The static page and the server page both call it.</summary>
    public static string VersionName(PublishedDocumentVersion before, PublishedCitation citation) =>
        citation.ExtractionId == before.ExtractionId ? "Earlier version" : "Later version";

    /// <summary>The marks of one saved version: its citations and the change date evidence. The static page and the server page both call it.</summary>
    public static IReadOnlyList<CitedRange> RangesIn(PublishedCitation[] citations, PublishedCitation? changeDateEvidence, PublishedDocumentVersion version)
    {
        var cited = citations.Select((citation, index) => (citation, anchor: CitedText.Anchor(index)));
        if (changeDateEvidence is not null) cited = cited.Append((changeDateEvidence, ChangeDateAnchor));
        return [.. cited.Where(item => item.citation.ExtractionId == version.ExtractionId)
            .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];
    }

    public static IReadOnlyList<Fact> Facts(DocumentChangePageData data, string rootPath)
    {
        var facts = new List<Fact> { Fact.LinkList("Page address", Addresses(data)), new("Source institution", data.Institution) };
        if (data.ChangeDate is { } date) facts.Add(new("Actual change date", Moment.Day(date)));
        facts.Add(new("First save", Moment.Full(data.Before.ObservedAtUtc)));
        facts.Add(new("Second save", Moment.Full(data.After.ObservedAtUtc)));
        if (data.Officials.Length > 0)
            facts.Add(Fact.LinkList("Officials", [.. data.Officials.Select(official => new FactLink(official.Name, rootPath + PublicationProtocol.OfficialPagePath(official.OfficialId)))]));
        if (data.Issues.Length > 0)
            facts.Add(Fact.LinkList("Issues", [.. data.Issues.Select(issue => new FactLink(issue.Name, issue.IsConfigured ? rootPath + PublicationProtocol.IssuePagePath(issue.IssueId, 1) : null))]));
        facts.Add(new("Revision", data.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return facts;
    }

    private static FactLink[] Addresses(DocumentChangePageData data) => data.Before.Url == data.After.Url
        ? [new(data.After.Url, data.After.Url)]
        : [new(data.Before.Url, data.Before.Url), new(data.After.Url, data.After.Url)];
}
