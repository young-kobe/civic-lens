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

    public const string KeyFactsHeading = "Key facts";

    public const string ReadIt = "Read it in the Federal Register";

    public const string WhoReviewedText = "Code checked each fact against the saved Federal Register record. No person reviewed it.";

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
        new("Who reviewed it", WhoReviewedText)
    ];

    private static Fact Correction(ExecutiveActionNoticeData notice)
    {
        var parts = CorrectionParts(notice.PublishedOn);
        var links = parts.Select(part => new FactLink(part.Text, part.IsLink ? notice.Url : null));
        return new Fact("Correction notice", string.Concat(parts.Select(part => part.Text)), [.. links]) { Separator = "" };
    }
}
