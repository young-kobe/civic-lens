using System.Globalization;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedExecutiveActionView
{
    public const string Caveat =
        "The headline is the official title from the Federal Register. Civic Lens did not summarize this document. " +
        "Code checked each fact against the saved Federal Register record. No person reviewed it. The title alone does not show what the document does. Read the document in the Federal Register.";

    public static IReadOnlyList<Fact> Facts(PublishedExecutiveAction action) =>
    [
        new("Type", PublishedRecordLabels.ExecutiveActionKind(action.Kind)),
        PublishedRecordView.Officials("President", [action.President]),
        new("Signed", Moment.Iso(action.SignedOn)),
        new("Published in the Federal Register", Moment.Iso(action.PublishedOn)),
        new("Federal Register citation", action.FederalRegisterCitation),
        new("Federal Register document number", action.DocumentNumber),
        .. action.CorrectionNotices.Select(notice => CorrectionFact(notice)),
        Fact.LinkList("Read the document", [new FactLink("Open the Federal Register page", action.DocumentUrl)]),
        new("Published", $"Checked by code {Moment.Full(action.ApprovedAtUtc)}; first published {Moment.Full(action.FirstPublishedAtUtc)}"),
        new("Revision", action.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    private static string CorrectionText(PublishedExecutiveActionNotice notice) => string.Concat(CorrectionParts(notice).Select(part => part.Text));

    public static string CorrectionTimelineText(PublishedExecutiveActionNotice notice) => CorrectionParts(notice)[0].Text.TrimEnd();

    private static Fact CorrectionFact(PublishedExecutiveActionNotice notice) =>
        new("Correction notice", CorrectionText(notice), [.. CorrectionParts(notice).Select(part => new FactLink(part.Text, part.IsLink ? notice.Url : null))])
        { Separator = "" };

    private static (string Text, bool IsLink)[] CorrectionParts(PublishedExecutiveActionNotice notice) =>
    [
        ($"The Federal Register published a correction on {Moment.Iso(notice.PublishedOn)}. This page shows the original document. ", false),
        ("Open the correction", true),
        (" to see what changed.", false)
    ];

    public static IReadOnlyList<CitedRange> RangesIn(PublishedExecutiveAction action, PublishedDocumentVersion source) =>
    [.. action.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];

    public static string Anchor(PublishedExecutiveAction action, PublishedExecutiveActionFact fact) =>
        PublishedRecordView.CitationAnchor(Array.IndexOf(action.Facts, fact));
}
