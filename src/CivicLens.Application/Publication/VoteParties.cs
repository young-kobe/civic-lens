using CivicLens.Application.Collection;
using CivicLens.Application.Roster;

namespace CivicLens.Application.Publication;

/// <summary>
/// Finds the party of a roll call member on the day of the vote. A caucus beats the party, so an Independent who caucuses with a party counts with it.
/// A member the roster does not hold gets the party of the configured office of that person, if any.
/// </summary>
internal sealed class VoteParties
{
    private readonly Dictionary<string, RosterTerm[]> termsBySourceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OfficeConfiguration[]> officesBySourceId = new(StringComparer.Ordinal);

    public VoteParties(IEnumerable<RosterMember> roster, IEnumerable<PersonConfiguration> people)
    {
        foreach (var member in roster)
        {
            termsBySourceId[member.BioguideId] = member.Terms;
            if (member.LisId is not null) termsBySourceId[member.LisId] = member.Terms;
        }
        foreach (var person in people.Where(person => person.BioguideId is not null && person.Offices is not null))
            officesBySourceId[person.BioguideId!] = person.Offices!;
    }

    public string? PartyOn(string sourceId, DateOnly day) =>
        termsBySourceId.TryGetValue(sourceId, out var terms) && TermOn(terms, day) is { } term
            ? term.Caucus ?? term.Party
            : OfficePartyOn(sourceId, day);

    private static RosterTerm? TermOn(RosterTerm[] terms, DateOnly day) =>
        terms.Where(term => term.Start <= day && day <= term.End).OrderByDescending(term => term.Start).FirstOrDefault();

    private string? OfficePartyOn(string sourceId, DateOnly day) =>
        officesBySourceId.TryGetValue(sourceId, out var offices)
            ? offices.FirstOrDefault(office => office.StartsOn <= day && (office.EndsOn is null || day <= office.EndsOn))?.Party
            : null;
}
