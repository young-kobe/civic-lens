using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Publication;

internal static class PublishedStatementView
{
    public static string ShortName(PublishedStatement statement) => string.Join(", ", statement.Speakers.Select(speaker => speaker.Name));

    public static IReadOnlyList<Fact> Facts(PublishedStatement statement, PublicationCatalog catalog)
    {
        var facts = new List<Fact>();
        facts.AddRange(PublishedRecordView.Officials("Speakers", statement.Speakers));
        facts.AddRange(PublishedRecordView.Values(PublishedRecordLabels.OtherPeopleQuoted, statement.OtherSpeakers));
        if (statement.StatementDate is { } date) facts.Add(new("Statement date", PublishedRecordLabels.StatementDate(statement.ApprovedBy, statement.StatementDateEvidence is not null, Moment.Day(date))));
        facts.AddRange(PublishedRecordView.Issues("Issues", statement.IssueIds, catalog));
        facts.AddRange(PublishedRecordView.PolicyAreas(PublishedRecordLabels.StatementPolicyAreas(statement.ApprovedBy), statement.PolicyAreaIds, catalog));
        facts.AddRange(PublishedRecordView.Values("Bill numbers", statement.BillNumbers));
        facts.Add(new("Published", $"{PublishedRecordLabels.ApprovalOn(statement.ApprovedBy, Moment.Full(statement.ApprovedAtUtc))}; first published {Moment.Full(statement.FirstPublishedAtUtc)}"));
        facts.AddRange(PublishedRecordLabels.StatementWording(statement));
        facts.Add(new("Revision", statement.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return facts;
    }
}
