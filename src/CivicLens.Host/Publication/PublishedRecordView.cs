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

    public static string RecordLink(string recordId) => "../" + PublicationProtocol.RecordPagePath(recordId);

    public static string OfficialLink(string officialId) => "../" + PublicationProtocol.OfficialPagePath(officialId);

    public static Fact Officials(string label, IEnumerable<PublishedOfficial> officials) =>
        Fact.LinkList(label, [.. officials.Select(official => new FactLink(official.Name, OfficialLink(official.Id)))]);

    public static FactLink IssueLink(string issueId, PublicationCatalog catalog, string rootPath) =>
        catalog.HasIssue(issueId)
            ? new(catalog.IssueName(issueId), rootPath + PublicationProtocol.IssuePagePath(issueId, 1))
            : new(issueId);

    public static Fact Issues(string label, IEnumerable<string> issueIds, PublicationCatalog catalog) =>
        Fact.LinkList(label, [.. issueIds.Select(id => IssueLink(id, catalog, "../"))]);

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

    private static string Slice(string text, int start, int length) => text.Substring(start, length);
}
