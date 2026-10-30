using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Host.Components.Evidence;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedRecordView
{
    public const string ChangeDateAnchor = "cite-change-date";

    public static string CitationAnchor(int index) => $"cite-{index + 1}";

    public static string QuoteTarget(string subject, int index = 0, int count = 1) =>
        count == 1 ? $"{subject}, quote" : $"{subject}, quote {index + 1}";

    public static string RecordLink(string recordId) => "../" + PublicationProtocol.RecordPagePath(recordId);

    public static string OfficialLink(string officialId) => "../" + PublicationProtocol.OfficialPagePath(officialId);

    public static IEnumerable<Fact> Values(string label, IReadOnlyCollection<string> values) =>
        values.Count == 0 ? [] : [Fact.List(label, values)];

    public static IEnumerable<Fact> Links(string label, IReadOnlyCollection<FactLink> links) =>
        links.Count == 0 ? [] : [Fact.LinkList(label, [.. links])];

    public static IEnumerable<Fact> Officials(string label, IEnumerable<PublishedOfficial> officials) =>
        Links(label, [.. officials.Select(official => new FactLink(official.Name, OfficialLink(official.Id)))]);

    public static FactLink IssueLink(string issueId, PublicationCatalog catalog, string rootPath) =>
        ConfiguredNameLink(catalog.HasIssue(issueId), catalog.IssueName(issueId),
            rootPath + PublicationProtocol.IssuePagePath(issueId, 1));

    public static IEnumerable<Fact> Issues(string label, IEnumerable<string> issueIds, PublicationCatalog catalog) =>
        Links(label, [.. issueIds.Select(id => IssueLink(id, catalog, "../"))]);

    public static FactLink PolicyAreaLink(string? policyAreaId, string text, PublicationCatalog catalog) =>
        policyAreaId is null
            ? new(text)
            : ConfiguredNameLink(catalog.HasPolicyArea(policyAreaId), text, "../" + PublicationProtocol.PolicyAreaPagePath(policyAreaId, 1));

    public static IEnumerable<Fact> PolicyAreas(string label, IEnumerable<string> policyAreaIds, PublicationCatalog catalog) =>
        Links(label, [.. policyAreaIds.Select(id => PolicyAreaLink(id, catalog.PolicyAreaName(id), catalog))]);

    public static ImmutableArray<DocumentComparisonHunk> Hunks(PublishedDocumentChange record) =>
        [.. record.Changes.Select(change => new DocumentComparisonHunk(
            change.BeforeStart, change.BeforeLength, change.AfterStart, change.AfterLength,
            Slice(record.Before.Text, change.BeforeStart, change.BeforeLength),
            Slice(record.After.Text, change.AfterStart, change.AfterLength),
            change.BeforeContextStart, Slice(record.Before.Text, change.BeforeContextStart, change.BeforeContextLength),
            change.AfterContextStart, Slice(record.After.Text, change.AfterContextStart, change.AfterContextLength),
            [.. change.WordEdits.Select(edit => new DocumentWordEdit(
                edit.BeforeStart, edit.BeforeLength, edit.AfterStart, edit.AfterLength))]))];

    public static IReadOnlyList<CitedRange> RangesIn(PublishedDocumentChange record, PublishedDocumentVersion version)
    {
        var cited = record.Citations.Select((citation, index) => (citation, anchor: CitationAnchor(index)));
        if (record.ChangeDateEvidence is { } evidence)
            cited = cited.Append((evidence, ChangeDateAnchor));
        return [.. cited.Where(item => item.citation.ExtractionId == version.ExtractionId)
            .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];
    }

    private static FactLink ConfiguredNameLink(bool isConfigured, string text, string href) =>
        new(text, isConfigured ? href : null);

    private static string Slice(string text, int start, int length) => text.Substring(start, length);
}
