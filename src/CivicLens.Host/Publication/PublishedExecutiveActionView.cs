using System.Globalization;
using CivicLens.Core.ExecutiveActions;
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
        new Fact("Type", PublishedRecordLabels.ExecutiveActionKind(action.Kind)) { TermKey = PublishedRecordLabels.ExecutiveActionTermKey(action.Kind) }
            .CitedAt(AnchorOf(action, ExecutiveActionFacts.Type)),
        .. PublishedRecordView.Officials("President", [action.President]).Select(fact => fact.CitedAt(AnchorOf(action, ExecutiveActionFacts.President))),
        new Fact("Signed", Moment.Day(action.SignedOn)).CitedAt(AnchorOf(action, ExecutiveActionFacts.Signed)),
        new Fact("Published in the Federal Register", Moment.Day(action.PublishedOn)).CitedAt(AnchorOf(action, ExecutiveActionFacts.Published)),
        new Fact("Federal Register citation", action.FederalRegisterCitation).CitedAt(AnchorOf(action, ExecutiveActionFacts.Citation)),
        new Fact("Federal Register document number", action.DocumentNumber).CitedAt(AnchorOf(action, ExecutiveActionFacts.DocumentNumber)),
        .. action.Facts.Where(fact => fact.Label == ExecutiveActionFacts.PresidentialNumber).Select(fact => new Fact(fact.Label, fact.Value).CitedAt(Anchor(action, fact))),
        .. action.CorrectionNotices.Select((notice, index) => CorrectionFact(action, notice, index)),
        Fact.LinkList("Read the document", [new FactLink("Open the Federal Register page", action.DocumentUrl)]).CitedAt(AnchorOf(action, ExecutiveActionFacts.Url)),
        new("Published", $"Checked by code {Moment.Full(action.ApprovedAtUtc)}; first published {Moment.Full(action.FirstPublishedAtUtc)}"),
        new("Revision", action.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    private static string CorrectionText(PublishedExecutiveActionNotice notice) => string.Concat(CorrectionParts(notice).Select(part => part.Text));

    public static string CorrectionTimelineText(PublishedExecutiveActionNotice notice) => CorrectionParts(notice)[0].Text.TrimEnd();

    private static Fact CorrectionFact(PublishedExecutiveAction action, PublishedExecutiveActionNotice notice, int index) =>
        new Fact("Correction notice", CorrectionText(notice), [.. CorrectionParts(notice).Select(part => new FactLink(part.Text, part.IsLink ? notice.Url : null))])
        { Separator = "" }.CitedAt(AnchorOf(action, ExecutiveActionFacts.CorrectionNotice, notice.Value),
            PublishedRecordView.SourceLineTarget("Correction notice", index, action.CorrectionNotices.Length));

    private static (string Text, bool IsLink)[] CorrectionParts(PublishedExecutiveActionNotice notice) =>
    [
        ($"The Federal Register published a correction on {Moment.Day(notice.PublishedOn)}. This page shows the original document. ", false),
        ("Open the correction", true),
        (" to see what changed.", false)
    ];

    public static IReadOnlyList<CitedRange> RangesIn(PublishedExecutiveAction action, PublishedDocumentVersion source) =>
    [.. action.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];

    private static string Anchor(PublishedExecutiveAction action, PublishedExecutiveActionFact fact) =>
        PublishedRecordView.CitationAnchor(Array.IndexOf(action.Facts, fact));

    private static string? AnchorOf(PublishedExecutiveAction action, string label, string? value = null) =>
        action.Facts.FirstOrDefault(fact => fact.Label == label && (value is null || fact.Value == value)) is { } found ? Anchor(action, found) : null;
}
