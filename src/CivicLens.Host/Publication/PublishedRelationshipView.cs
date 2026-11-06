using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Publication;

internal static class PublishedRelationshipView
{
    public static IReadOnlyList<Fact> Facts(PublishedRelationship relationship, PublicationCatalog catalog)
    {
        var facts = new List<Fact> { new("Type", PublishedRecordLabels.RelationshipType(relationship.Type)) };
        facts.AddRange(PublishedRecordView.Officials("Speakers", relationship.Officials));
        if (relationship.StatementDate is { } date) facts.Add(new("Statement date", Moment.Day(date)));
        facts.AddRange(PublishedRecordView.Issues("Issues", relationship.IssueIds, catalog));
        facts.AddRange(PublishedRecordView.PolicyAreas("Policy areas", relationship.PolicyAreaIds, catalog));
        facts.Add(new("Published", $"Approved {Moment.Full(relationship.ApprovedAtUtc)}; first published {Moment.Full(relationship.FirstPublishedAtUtc)}"));
        facts.Add(new("Revision", relationship.RevisionNumber.ToString(CultureInfo.InvariantCulture)));
        return facts;
    }
}
