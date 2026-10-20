using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

public static class ExecutiveRosterParser
{
    private const string President = "prez";
    private const string VicePresident = "viceprez";

    public static ExecutiveOfficial[] Parse(ReadOnlySpan<byte> json)
    {
        var wire = CongressApiJson.Deserialize<WireOfficial?[]>(json, "Executive roster");
        var officials = wire.Select(ToOfficial).ToArray();
        if (officials.Length == 0)
            throw new InvalidDataException("Executive roster must list at least one official.");
        if (officials.DistinctBy(official => official.GovtrackId).Count() != officials.Length)
            throw new InvalidDataException("Executive roster must list each GovTrack ID once.");
        var wikidataIds = officials.Where(official => official.WikidataId is not null).Select(official => official.WikidataId).ToArray();
        if (wikidataIds.Distinct(StringComparer.Ordinal).Count() != wikidataIds.Length)
            throw new InvalidDataException("Executive roster must list each Wikidata ID once.");
        return officials;
    }

    private static ExecutiveOfficial ToOfficial(WireOfficial? wire)
    {
        if (wire?.Id is not { Govtrack: { } govtrackId and > 0 } id || wire.Terms is not { Length: > 0 } wireTerms)
            throw new InvalidDataException("Every executive official must have a GovTrack ID and at least one term.");
        return new ExecutiveOfficial(govtrackId, WikidataOf(id), RosterWire.FullNameOf(wire.Name),
            [.. wireTerms.Select(ToTerm)]);
    }

    private static string? WikidataOf(WireId id)
    {
        var wikidataId = RosterWire.Capped(id.Wikidata, RosterLimits.WikidataId);
        return wikidataId is null || WikidataId.IsValid(wikidataId)
            ? wikidataId
            : throw new InvalidDataException("An executive official has a malformed Wikidata ID.");
    }

    private static ExecutiveTerm ToTerm(WireTerm? wire, int index)
    {
        if (wire is not { Start: { } start, End: { } end } || end < start || wire.Type is not (President or VicePresident) ||
            RosterWire.Capped(wire.Party, RosterLimits.Party) is not { } party)
            throw new InvalidDataException("Every executive term must have an office, dates, and a party.");
        return new ExecutiveTerm(index, wire.Type, start, end, party);
    }

    private sealed record WireOfficial(WireId? Id, RosterWire.WireName? Name, WireTerm?[]? Terms);

    private sealed record WireId(int? Govtrack, string? Wikidata);

    private sealed record WireTerm(string? Type, DateOnly? Start, DateOnly? End, string? Party);
}
