using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words, anchors, and facts of the statement page.</summary>
public static class StatementPageView
{
    public const int ShownQuotations = 3;

    public const string DateEvidenceAnchor = "cite-statement-date";

    public const string SourceSeen = "Source seen";

    public const string CheckLine = "The quotes below come straight from the source and code checked each one.";

    public const string ModelCheckLine = CheckLine + " A model wrote only the summary above.";

    /// <summary>The caveat of the static statement page. It sits in the check line, so no separate caveat shows.</summary>
    public const string CaptureCaveat = "The observation time shows when the source was captured. It does not show when the statement was first made.";

    public const string QuotationsHeading = "What was said";

    public const string DateNotStated = "Date not stated";

    public const string FeedNote = "The source below is the feed that lists the article. It holds a copy of the article text.";

    public static string CheckFor(PublishedTextOrigin origin) => $"{(origin == PublishedTextOrigin.Model ? ModelCheckLine : CheckLine)} {CaptureCaveat}";

    /// <summary>The eyebrow names the speakers. One speaker also gets the seat and the party, as in the mockup. Several speakers keep their names only, and the rail gives each role.</summary>
    public static string Eyebrow(StatementPageData data) => data.Speakers switch
    {
        [] => "Statement",
        [{ Role: { } role } speaker] => $"Statement | {speaker.Name}, {role}",
        _ => $"Statement | {data.ShortName}"
    };

    /// <summary>The date sentence of the lede. An undated statement says so, and names when the source was seen.</summary>
    public static string DateLine(StatementPageData data) => data.StatementDate is { } date
        ? $"Said on {Moment.Day(date)}."
        : $"{DateNotStated}. {SourceSeenLine(data)}";

    /// <summary>The sentence that says when the source was seen. The lede of an undated statement and the source card both show it.</summary>
    public static string SourceSeenLine(StatementPageData data) => $"{SourceSeen} {Moment.Full(data.Source.ObservedAtUtc)}.";

    public static string QuotationsNote(StatementPageData data) => data.Quotations.Length > ShownQuotations
        ? $"Each quote is the exact text of the source. {data.Quotations.Length} quotes are saved. The first {ShownQuotations} are shown."
        : "Each quote is the exact text of the source.";

    public static string QuotationLabel(int index) => $"Quote {index + 1}";

    public static string QuotationNote(PublishedQuotation quotation) => $"{PublishedRecordLabels.Quotation(quotation.Kind)} in the source.";

    public static string AllQuotationsLink(StatementPageData data) => $"See all {data.Quotations.Length} quotes in the source fold";

    /// <summary>The marks of the saved text: one per quote and one for the date evidence. The static page and the server page both call it.</summary>
    public static IReadOnlyList<CitedRange> Ranges(PublishedQuotation[] quotations, PublishedCitation? dateEvidence)
    {
        var ranges = quotations.Select((quotation, index) => new CitedRange(CitedText.Anchor(index), quotation.Start, quotation.Length));
        if (dateEvidence is not null) ranges = ranges.Append(new CitedRange(DateEvidenceAnchor, dateEvidence.Start, dateEvidence.Length));
        return [.. ranges];
    }

    public static IReadOnlyList<Fact> Facts(StatementPageData data)
    {
        var facts = new List<Fact> { Fact.LinkList("Original page", [new FactLink(data.OriginalUrl, data.OriginalUrl)]) };
        if (data.ArticleUrl is not null) facts.Add(Fact.LinkList("Saved copy from", [new FactLink(data.Source.Url, data.Source.Url)]));
        facts.Add(new(SourceSeen, Moment.Full(data.Source.ObservedAtUtc)));
        facts.Add(new("Date of the statement", DateFact(data)));
        if (data.DateEvidence is { } evidence)
            facts.Add(new Fact("Evidence for the statement date", evidence.Quote).CitedAt(DateEvidenceAnchor));
        facts.AddRange(PublishedTimeFacts.Of(data.ApprovedAtUtc, data.FirstPublishedAtUtc));
        facts.Add(new("Revision", data.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return facts;
    }

    /// <summary>The date rule has one owner: the label of the static page. It names an AI date only when code approved the statement and the date has evidence.</summary>
    private static string DateFact(StatementPageData data) => data.StatementDate is { } date
        ? PublishedRecordLabels.StatementDate(data.ApprovedBy, data.DateEvidence is not null, Moment.Day(date))
        : "Not stated in the source";
}
