using CivicLens.Application.Publication;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedStatementView
{
    public const string StatementDateAnchor = "cite-statement-date";

    public static IReadOnlyList<Fact> Facts(PublishedStatement statement, PublicationCatalog catalog)
    {
        var facts = new List<Fact>();
        facts.AddRange(PublishedRecordView.Officials("Speakers", statement.Speakers));
        facts.AddRange(PublishedRecordView.Values(PublishedRecordLabels.OtherPeopleQuoted, statement.OtherSpeakers));
        if (statement.StatementDate is { } date) facts.Add(new("Statement date", PublishedRecordLabels.StatementDate(statement, Moment.Day(date))));
        facts.AddRange(PublishedRecordView.Issues("Issues", statement.IssueIds, catalog));
        facts.AddRange(PublishedRecordView.PolicyAreas(PublishedRecordLabels.StatementPolicyAreas(statement.ApprovedBy), statement.PolicyAreaIds, catalog));
        facts.AddRange(PublishedRecordView.Values("Bill numbers", statement.BillNumbers));
        facts.Add(new("Published", $"{PublishedRecordLabels.ApprovalOn(statement.ApprovedBy, Moment.Full(statement.ApprovedAtUtc))}; first published {Moment.Full(statement.FirstPublishedAtUtc)}"));
        facts.AddRange(PublishedRecordLabels.StatementWording(statement));
        facts.Add(new("Revision", statement.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return facts;
    }

    public static IReadOnlyList<CitedRange> Ranges(PublishedStatement statement)
    {
        var ranges = statement.Quotations.Select((quotation, index) =>
            new CitedRange(PublishedRecordView.CitationAnchor(index), quotation.Start, quotation.Length));
        if (statement.StatementDateEvidence is { } evidence)
            ranges = ranges.Append(new CitedRange(StatementDateAnchor, evidence.Start, evidence.Length));
        return [.. ranges];
    }
}
