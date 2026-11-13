using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the fixed words of an executive action page. The static page and the server page both call it.</summary>
public static class ExecutiveActionWords
{
    public const string Caveat =
        "The headline is the official title from the Federal Register. Civic Lens did not summarize this document. " +
        "Code checked each fact against the saved Federal Register record. No person reviewed it. The title alone does not show what the document does. Read the document in the Federal Register.";

    /// <summary>The check line under an AI summary. It is the line of the approved mockup. The label of the summary is in the view.</summary>
    public const string SummaryCheck =
        "The title, dates, citation and numbers on this page come from the Federal Register, checked by code. A model wrote only this summary.";

    /// <summary>The Wording line of the source fold of a page with an AI summary. It is the line of the approved mockup, with "document" for "order".</summary>
    public const string SummaryWording = "The headline is the official title. The summary is an AI summary of the document text.";

    public const string KeyFactsHeading = "Key facts";

    public const string LinksHeading = "Links to other documents";

    public static string TargetRelation(PublishedExecutiveActionRelation relation) => relation switch
    {
        PublishedExecutiveActionRelation.Amends => "Amends",
        PublishedExecutiveActionRelation.Revokes => "Revokes",
        _ => throw new ArgumentOutOfRangeException(nameof(relation))
    };

    /// <summary>The line under the explainer of each kind: what to look for in the document text.</summary>
    public static string ReadHint(PublishedExecutiveActionKind kind) => kind switch
    {
        PublishedExecutiveActionKind.ExecutiveOrder => "Read it to see which agencies it directs and what they must do.",
        PublishedExecutiveActionKind.Proclamation => "Read the text to see what it announces or orders.",
        PublishedExecutiveActionKind.Memorandum => "Read it to see who it directs and what they must do.",
        PublishedExecutiveActionKind.Notice => "Read it to see what it announces.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The kind name in running text, such as "executive order".</summary>
    public static string KindName(PublishedExecutiveActionKind kind) => PublishedRecordLabels.ExecutiveActionKind(kind).ToLowerInvariant();

    /// <summary>The heading of the fixed explainer, such as "What an executive order is".</summary>
    public static string ExplainerHeading(PublishedExecutiveActionKind kind) => $"What {Article(KindName(kind))} {KindName(kind)} is";

    /// <summary>The words after "An executive order is". They are the glossary meaning of the kind, so the page and the glossary share one text.</summary>
    public static string ExplainerMeaning(PublishedExecutiveActionKind kind)
    {
        var means = Glossary.Term(PublishedRecordLabels.ExecutiveActionTermKey(kind)).Means;
        return char.ToLowerInvariant(means[0]) + means[1..];
    }

    /// <summary>The sentences after the meaning: why the kind matters, from the glossary.</summary>
    public static string ExplainerWhy(PublishedExecutiveActionKind kind) => Glossary.Term(PublishedRecordLabels.ExecutiveActionTermKey(kind)).WhyItMatters;

    /// <summary>"A" or "An", the start of the first explainer sentence.</summary>
    public static string ExplainerArticle(PublishedExecutiveActionKind kind)
    {
        var article = Article(KindName(kind));
        return char.ToUpperInvariant(article[0]) + article[1..];
    }

    /// <summary>The whole explainer sentence as plain text, such as "An executive order is a signed direction ...". The President page shows it for each kind,
    /// and the action page shows the same words with the kind name as a glossary term. So each kind has one meaning on every page.</summary>
    public static string ExplainerText(PublishedExecutiveActionKind kind) => $"{ExplainerArticle(kind)} {KindName(kind)} is {ExplainerMeaning(kind)} {ExplainerWhy(kind)}";

    private static string Article(string word) => "aeiou".Contains(word[0]) ? "an" : "a";

    public const string ReadIt = "Read it in the Federal Register";

    /// <summary>The text of a correction notice in three parts. The middle part is the link.</summary>
    public static (string Text, bool IsLink)[] CorrectionParts(DateOnly publishedOn) =>
    [
        ($"The Federal Register published a correction on {Moment.Day(publishedOn)}. This page shows the original document. ", false),
        ("Open the correction", true),
        (" to see what changed.", false)
    ];

    public static string Eyebrow(ExecutiveActionPageData data) => $"Executive action | President {data.PresidentName}";

    public static string DocumentBadge(ExecutiveActionPageData data) => $"Federal Register document {data.DocumentNumber}";

    public static string Lede(ExecutiveActionPageData data) =>
        $"Signed by President {data.PresidentName} on {Moment.Day(data.SignedOn)}. Printed in the Federal Register on {Moment.Day(data.PublishedOn)}, citation {data.FederalRegisterCitation}.";

    public static string Approval(ExecutiveActionPageData data) => $"Checked by code. Revision {data.RevisionNumber}.";

    public static IReadOnlyList<Fact> KeyFacts(ExecutiveActionPageData data, string rootPath) =>
    [
        new("Signed", Moment.Day(data.SignedOn)),
        new("Published in the Federal Register", Moment.Day(data.PublishedOn)),
        new("Federal Register citation", data.FederalRegisterCitation),
        Fact.LinkList("President", [new FactLink(data.PresidentName, rootPath + PublicationProtocol.OfficialPagePath(data.OfficialId))]),
        .. data.Notices.Select(Correction)
    ];

    public static IReadOnlyList<Fact> SourceFacts(ExecutiveActionPageData data) =>
    [
        Fact.LinkList("Official document", [new FactLink($"Federal Register document {data.DocumentNumber}", data.DocumentUrl)]),
        new Fact("Document type", PublishedRecordLabels.ExecutiveActionKind(data.Kind)) { TermKey = PublishedRecordLabels.ExecutiveActionTermKey(data.Kind) },
        .. data.Summary is { Label: not null } ? [new Fact("Wording", SummaryWording)] : Array.Empty<Fact>()
    ];

    private static Fact Correction(ExecutiveActionNoticeData notice)
    {
        var parts = CorrectionParts(notice.PublishedOn);
        var links = parts.Select(part => new FactLink(part.Text, part.IsLink ? notice.Url : null));
        return new Fact("Correction notice", string.Concat(parts.Select(part => part.Text)), [.. links]) { Separator = "" };
    }
}
