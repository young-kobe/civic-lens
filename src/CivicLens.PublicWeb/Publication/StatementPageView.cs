using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words, anchors, and facts of the statement page.</summary>
public static class StatementPageView
{
    public const int ShownQuotations = 3;

    public const string DateEvidenceAnchor = "cite-statement-date";

    public const string AiLabel = "AI summary. Drafted by an AI model from the source text.";

    public const string CheckLine = "The quotes below come straight from the source and code checked each one.";

    public const string ModelCheckLine = CheckLine + " A model wrote only the summary above.";

    public const string QuotationsHeading = "What was said";

    public const string DateNotStated = "Date not stated";

    public const string FeedNote = "The source below is the feed that lists the article. It holds a copy of the article text.";

    public static string CheckFor(PublishedTextOrigin origin) => origin == PublishedTextOrigin.Model ? ModelCheckLine : CheckLine;

    public static string? Label(StatementPageData data) => DetailWords.SummaryLabel(data.TextOrigin, AiLabel);

    public static string QuotationAnchor(int index) => $"cite-{index + 1}";

    public static string Eyebrow(StatementPageData data) => data.Speakers.Length == 0 ? "Statement" : $"Statement | {data.ShortName}";

    /// <summary>The date sentence of the lede. An undated statement says so, and names when the source was seen.</summary>
    public static string DateLine(StatementPageData data) => data.StatementDate is { } date
        ? $"Said on {Moment.Day(date)}."
        : $"{DateNotStated}. Source seen {Moment.Full(data.Source.ObservedAtUtc)}.";

    public static string QuotationsNote(StatementPageData data) => data.Quotations.Length > ShownQuotations
        ? $"Each quote is the exact text of the source. {data.Quotations.Length} quotes are saved. The first {ShownQuotations} are shown."
        : "Each quote is the exact text of the source.";

    public static string QuotationLabel(int index) => $"Quote {index + 1}";

    public static string QuotationNote(PublishedQuotation quotation) => $"{PublishedRecordLabels.Quotation(quotation.Kind)} in the source.";

    public static string AllQuotationsLink(StatementPageData data) => $"See all {data.Quotations.Length} quotes in the source fold";

    public static IReadOnlyList<CitedRange> Ranges(StatementPageData data)
    {
        var ranges = data.Quotations.Select((quotation, index) => new CitedRange(QuotationAnchor(index), quotation.Start, quotation.Length));
        if (data.DateEvidence is { } evidence) ranges = ranges.Append(new CitedRange(DateEvidenceAnchor, evidence.Start, evidence.Length));
        return [.. ranges];
    }

    public static IReadOnlyList<Fact> Facts(StatementPageData data)
    {
        var facts = new List<Fact> { Fact.LinkList("Original page", [new FactLink(data.OriginalUrl, data.OriginalUrl)]) };
        if (data.ArticleUrl is not null) facts.Add(Fact.LinkList("Saved copy from", [new FactLink(data.Source.Url, data.Source.Url)]));
        facts.Add(new(DetailWords.SourceSeen, Moment.Full(data.Source.ObservedAtUtc)));
        facts.Add(new("Date of the statement", DateFact(data)));
        if (data.DateEvidence is { } evidence)
            facts.Add(new Fact("Evidence for the statement date", evidence.Quote).CitedAt(DateEvidenceAnchor));
        facts.Add(new("Revision", data.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return facts;
    }

    /// <summary>The date line names an AI date only when the model found it and code checked it against the quote. The model summary label marks that case.</summary>
    private static string DateFact(StatementPageData data) => data.StatementDate switch
    {
        null => "Not stated in the source",
        { } date when data.DateEvidence is not null && data.Label is not null => $"{Moment.Day(date)}. {PublishedRecordLabels.ModelDateCheckedByCode}.",
        { } date => Moment.Day(date)
    };
}
