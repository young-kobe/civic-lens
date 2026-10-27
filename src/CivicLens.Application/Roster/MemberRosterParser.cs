using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

public static class MemberRosterParser
{
    public const string Senate = "sen";
    private const string House = "rep";

    public static RosterMember[] Parse(ReadOnlySpan<byte> json)
    {
        var wire = CongressApiJson.Deserialize<WireMember?[]>(json, "Member roster");
        var members = wire.Select(ToMember).ToArray();
        if (members.Length == 0)
            throw new InvalidDataException("Member roster must list at least one member.");
        if (members.DistinctBy(member => member.BioguideId).Count() != members.Length)
            throw new InvalidDataException("Member roster must list each bioguide ID once.");
        return members;
    }

    private static RosterMember ToMember(WireMember? wire)
    {
        var bioguideId = wire?.Id?.Bioguide;
        if (bioguideId is null || !BioguideId.IsValid(bioguideId) || wire!.Terms is not { Length: > 0 } wireTerms)
            throw new InvalidDataException("Every roster member must have a bioguide ID and at least one term.");
        var terms = wireTerms.Select(ToTerm).ToArray();
        var current = wireTerms[^1]!;
        return new RosterMember(bioguideId, RosterWire.Capped(wire.Id!.Lis, RosterLimits.LisId), RosterWire.FullNameOf(wire.Name),
            RosterWire.Capped(current.Office, RosterLimits.Office), RosterWire.Capped(current.Phone, RosterLimits.Phone),
            RosterWire.Capped(current.Url, RosterLimits.Url), terms);
    }

    private static RosterTerm ToTerm(WireTerm? wire, int index)
    {
        if (wire is not { Start: { } start, End: { } end } || end < start || wire.Type is not (Senate or House) ||
            RosterWire.Capped(wire.State, RosterLimits.State) is not { } state || RosterWire.Capped(wire.Party, RosterLimits.Party) is not { } party)
            throw new InvalidDataException("Every roster term must have a chamber, dates, a state, and a party.");
        return new RosterTerm(index, wire.Type, start, end, state, wire.District, wire.Class, party,
            RosterWire.Capped(wire.Caucus, RosterLimits.Party));
    }

    private sealed record WireMember(WireId? Id, RosterWire.WireName? Name, WireTerm?[]? Terms);

    private sealed record WireId(string? Bioguide, string? Lis);

    private sealed record WireTerm(string? Type, DateOnly? Start, DateOnly? End, string? State, int? District, int? Class,
        string? Party, string? Caucus, string? Office, string? Phone, string? Url);
}
