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
        .. action.CorrectionOf is { } corrected ? [new Fact("Corrects document", corrected)] : Array.Empty<Fact>(),
        Fact.LinkList("Read the document", [new FactLink("Open the Federal Register page", action.DocumentUrl)]),
        new("Published", $"Checked by code {Moment.Full(action.ApprovedAtUtc)}; first published {Moment.Full(action.FirstPublishedAtUtc)}"),
        new("Revision", action.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    public static IReadOnlyList<CitedRange> RangesIn(PublishedExecutiveAction action, PublishedDocumentVersion source) =>
    [.. action.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];

    public static string Anchor(PublishedExecutiveAction action, PublishedExecutiveActionFact fact) =>
        PublishedRecordView.CitationAnchor(Array.IndexOf(action.Facts, fact));
}
