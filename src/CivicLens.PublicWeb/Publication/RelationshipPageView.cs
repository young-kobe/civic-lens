using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words and facts of the relationship page. The static relationship page of the Host uses the same words until the cutover.</summary>
public static class RelationshipPageView
{
    public const string Caveat = "A person approved this link between a statement and a record. It states no verdict. It does not say that the statement is true or that it agrees with the record.";

    public const string EvidenceHeading = "Evidence";

    public const string QuoteLabel = "Quote from the statement";

    public const string LinkedHeading = "Linked records";

    public const string LinkedNote = "Each row names the revision that was approved with this link. Its page shows the revision that the release holds.";

    public static string FactLabel(PublishedRecordKind targetKind) => $"Fact of the {PublishedRecordLabels.KindInSentence(targetKind)}";

    public static string CitedRevision(PublishedRecordKind kind, int revisionNumber) =>
        $"{PublishedRecordLabels.Kind(kind)}, revision {revisionNumber.ToString(CultureInfo.InvariantCulture)}";

    public static string Eyebrow(RelationshipPageData data) => $"{PublishedRecordLabels.Kind(PublishedRecordKind.Relationship)} | {PublishedRecordLabels.RelationshipType(data.Type)}";

    /// <summary>The date sentence of the lede. The date is the exact statement date of the published relationship, in the words of the statement rows.</summary>
    public static string DateLine(RelationshipPageData data) => $"{StatementPageView.RowDate(data.StatementDate)}.";

    public static IReadOnlyList<Fact> Facts(RelationshipPageData data)
    {
        var facts = new List<Fact> { new("Type", PublishedRecordLabels.RelationshipType(data.Type)) };
        if (data.StatementDate is { } date) facts.Add(new("Statement date", Moment.Day(date)));
        facts.AddRange(PublishedTimeFacts.Of(data.ApprovedAtUtc, data.FirstPublishedAtUtc));
        facts.Add(new("Revision", data.RevisionNumber.ToString(CultureInfo.InvariantCulture)));
        return facts;
    }
}
