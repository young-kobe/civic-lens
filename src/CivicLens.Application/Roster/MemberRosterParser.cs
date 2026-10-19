using System.Text.Json.Serialization;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

public static class MemberRosterParser
{
    private const string Senate = "sen";
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
        return new RosterMember(bioguideId, Present(wire.Id!.Lis), FullNameOf(wire.Name), Present(current.Office),
            Present(current.Phone), Present(current.Url), terms);
    }

    private static RosterTerm ToTerm(WireTerm? wire, int index)
    {
        if (wire is not { Start: { } start, End: { } end } || end < start || wire.Type is not (Senate or House) ||
            Present(wire.State) is not { } state || Present(wire.Party) is not { } party)
            throw new InvalidDataException("Every roster term must have a chamber, dates, a state, and a party.");
        return new RosterTerm(index, wire.Type, start, end, state, wire.District, wire.Class, party);
    }

    private static string FullNameOf(WireName? name) =>
        Present(name?.OfficialFull) ?? Present($"{name?.First} {name?.Last}")
        ?? throw new InvalidDataException("Every roster member must have a name.");

    private static string? Present(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private sealed record WireMember(WireId? Id, WireName? Name, WireTerm?[]? Terms);

    private sealed record WireId(string? Bioguide, string? Lis);

    private sealed record WireName(string? First, string? Last, [property: JsonPropertyName("official_full")] string? OfficialFull);

    private sealed record WireTerm(string? Type, DateOnly? Start, DateOnly? End, string? State, int? District, int? Class,
        string? Party, string? Office, string? Phone, string? Url);
}
