using System.Globalization;
using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CivicLens.PublicApi.Maps;

/// <summary>Reads one published map with the choices its form needs.</summary>
public sealed class MapsPageReader(MapStore store)
{
    private const string DefaultMeasure = "bills";
    private static readonly string[] Fields = ["congress", "chamber", "layer", "issue", "measure", "end"];

    public async Task<MapsPageData?> ReadAsync(IQueryCollection query, CancellationToken cancellationToken)
    {
        var request = MapRequest.Parse(WithApplicableIssue(query));
        var measureValue = QueryValues.Value(query, "measure");
        var measure = request.Layer == MapLayer.IssueActivity ? Measure(measureValue) : DefaultMeasure;
        DateOnly? end = null;
        if (request.Layer == MapLayer.TermEnds) end = QueryValues.Date(query, "end");
        else _ = QueryValues.Value(query, "end");
        if (await store.FindPageAsync(request, cancellationToken) is not { } answer) return null;

        var layer = QueryValues.ChoiceText(request.Layer);
        var dates = request.Layer == MapLayer.TermEnds ? EndDates(answer.Map) : [];
        if (end is { } requested && !dates.Contains(requested))
            return Data(answer, layer, RecordListFilter.Refused("end", QueryValues.Typed(query, Fields)), measure, null, dates);

        var selectedEnd = end ?? (dates.Length == 0 ? null : dates[0]);
        var filter = RecordListFilter.Of(null, null, ("congress", answer.Map.Congress?.ToString(CultureInfo.InvariantCulture)),
            ("chamber", QueryValues.ChoiceText(request.Chamber)), ("layer", layer), ("issue", request.Issue), ("measure", measure),
            ("end", selectedEnd is { } date ? Moment.Iso(date) : null));
        return Data(answer, layer, filter, measure, selectedEnd, dates);
    }

    /// <summary>Preserves the submitted text and reads choices for the usable parts of the selection.</summary>
    public async Task<MapsPageData?> ReadRefusedAsync(IQueryCollection query, string? invalid, CancellationToken cancellationToken)
    {
        var filter = RecordListFilter.Refused(invalid, QueryValues.Typed(query, Fields));
        var layer = MapsWords.Layers.Any(choice => choice.Value == filter.Value("layer")) ? filter.Value("layer")! : "attendance";
        var request = RefusedScope(query, invalid);
        MapPageAnswer? answer;
        try
        {
            answer = await store.FindPageAsync(request, cancellationToken);
        }
        catch (ArgumentException exception) when (exception.ParamName == "congress" && request.Congress is not null)
        {
            // A second invalid choice can be a Congress after the active release. Read the current scope in that case.
            answer = await store.FindPageAsync(request with { Congress = null }, cancellationToken);
        }

        if (answer is null) return null;
        var dates = layer == "termEnds" ? EndDates(answer.Map) : [];
        return Data(answer, layer, filter, DefaultMeasure, null, dates);
    }

    private static IQueryCollection WithApplicableIssue(IQueryCollection query)
    {
        if (!query.ContainsKey("issue") ||
            (query.TryGetValue("layer", out var layers) && layers.Count == 1 && layers[0] == "issueActivity"))
            return query;

        // A GET form may submit a previous layer's issue. Its value can be ignored, but repeated fields are still refused.
        _ = QueryValues.Value(query, "issue");
        var mapFields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "congress", "chamber", "layer" })
            if (query.TryGetValue(name, out var values)) mapFields.Add(name, values);
        return new QueryCollection(mapFields);
    }

    private static MapRequest RefusedScope(IQueryCollection query, string? invalid)
    {
        var scope = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "congress", "chamber" })
            if (name != invalid && query.TryGetValue(name, out var values)) scope.Add(name, values);

        // Parse the scope with the regular calendar and chamber rules. Skip figure reads for a refused form.
        while (true)
        {
            try
            {
                return MapRequest.Parse(new QueryCollection(scope)) with { Layer = MapLayer.TermEnds };
            }
            catch (ArgumentException exception) when (exception.ParamName is { } field && scope.Remove(field))
            {
            }
        }
    }

    private static string Measure(string? measure) => string.IsNullOrEmpty(measure) ? DefaultMeasure
        : MapsWords.Measures.Any(choice => choice.Value == measure) ? measure
        : throw new ArgumentException("measure must be bills or votes.", "measure");

    private static DateOnly[] EndDates(MapAnswer map) =>
        [.. map.Members.Select(member => member.TermEndsOn).OfType<DateOnly>().Distinct().Order()];

    private static MapsPageData Data(MapPageAnswer answer, string layer, RecordListFilter filter, string measure, DateOnly? end, DateOnly[] dates)
    {
        var map = answer.Map;
        MapMemberData[] members = [];
        MapSeatData[] seats = [];
        if (filter.Invalid is null)
        {
            var holders = map.Seats.Select(seat => seat.OfficialId).OfType<string>().ToHashSet(StringComparer.Ordinal);
            members = [.. map.Members.Select(member => Member(member, holders.Contains(member.OfficialId)))];
            seats = [.. map.Seats.Select(seat => new MapSeatData(seat.State, seat.District, seat.SenateClass, seat.OfficialId))];
        }
        return new(map.Release, map.Congress, answer.ReleaseCongress, map.Chamber, layer, map.AsOf, map.Issue?.Name, members, seats,
            [.. answer.IssueChoices.Select(issue => new SearchChoiceData(issue.Id, issue.Name))], filter, measure, end)
        { EndDates = dates };
    }

    private static MapMemberData Member(MapMemberAnswer member, bool hasSeat) => new(member.OfficialId, member.Name, member.PagePath,
        string.Join(", ", member.Terms.Select(term => OfficialSeat.Text(term.Kind, term.Office, term.State, term.District) ?? term.Office).Distinct(StringComparer.Ordinal)),
        member.Party, member.TermEndsOn, member.Attendance is { } attendance ? new(attendance.Held - attendance.Missed, attendance.Held) : null,
        Fraction(member.IssueActivity?.SponsoredBills), Fraction(member.IssueActivity?.SubstantiveCasts), hasSeat);

    private static MapFraction? Fraction(MapCounts? counts) => counts is null ? null : new(counts.Matching, counts.Base);
}
