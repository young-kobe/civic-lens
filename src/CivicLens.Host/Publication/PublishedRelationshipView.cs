using System.Globalization;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedRelationshipView
{
    public static IReadOnlyList<Fact> Facts(PublishedRelationship relationship)
    {
        var facts = new List<Fact>
        {
            new("Type", PublishedRecordLabels.RelationshipType(relationship.Type)),
            Fact.List("Speakers", relationship.Officials.Select(official => official.Name))
        };
        if (relationship.StatementDate is { } date) facts.Add(new("Statement date", Moment.Iso(date)));
        facts.Add(Fact.List("Issues", relationship.IssueIds));
        facts.Add(Fact.List("Policy areas", relationship.PolicyAreaIds));
        facts.Add(new("Published", $"Approved {Moment.Full(relationship.ApprovedAtUtc)}; first published {Moment.Full(relationship.FirstPublishedAtUtc)}"));
        facts.Add(new("Revision", relationship.RevisionNumber.ToString(CultureInfo.InvariantCulture)));
        return facts;
    }
}
