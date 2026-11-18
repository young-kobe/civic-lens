using System.Text.Json.Serialization;
using CivicLens.PublicApi.Http;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Maps;

public sealed record MapRequest(int? Congress, PublishedChamber Chamber, MapLayer Layer, string? Issue)
{
    public static MapRequest Parse(IQueryCollection query)
    {
        var congress = QueryValues.Number(query, "congress", 1, CongressCalendar.CongressOn(DateOnly.MaxValue)!.Value);
        if (congress is { } selected && CongressCalendar.CongressOn(CongressCalendar.StartOf(selected)) != selected)
            throw new ArgumentException("congress is outside the supported calendar.", "congress");

        var chamber = QueryValues.Choice<PublishedChamber>(query, "chamber") ?? PublishedChamber.House;
        var layer = QueryValues.Choice<MapLayer>(query, "layer") ?? MapLayer.Attendance;
        var issue = QueryValues.Filter(query, "issue");
        if ((layer == MapLayer.IssueActivity) != (issue is not null))
            throw new ArgumentException("issue is required for issueActivity and cannot be used with other layers.", "issue");

        return new(congress, chamber, layer, issue);
    }
}

[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum MapLayer { Attendance, TermEnds, IssueActivity }
