using CivicLens.Application.Roster;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Finds the party of a roll call member on the day of the vote. The roster term of the vote chamber that covers the day comes first, and a caucus beats the party,
/// so an Independent who caucuses with a party counts with it. The roster holds current members only, so a member who left takes the party named by the letter on the member line.
/// A member with neither counts as Unknown. When two terms cover the day, the term that starts later wins.
/// </summary>
internal sealed class VoteParties
{
    private readonly Dictionary<string, RosterTerm[]> termsBySourceId = new(StringComparer.Ordinal);

    public VoteParties(IEnumerable<RosterMember> roster)
    {
        foreach (var member in roster)
            termsBySourceId[member.BioguideId] = member.Terms;
    }

    public string PartyOn(VoteMemberLine line, Chamber chamber, DateOnly day) =>
        termsBySourceId.TryGetValue(line.SourceId, out var terms) && TermOn(terms, chamber, day) is { } term
            ? term.Caucus ?? term.Party
            : line.SourcePartyName ?? PublishedPartyMajority.UnknownParty;

    private static RosterTerm? TermOn(RosterTerm[] terms, Chamber chamber, DateOnly day)
    {
        var rosterChamber = chamber == Chamber.Senate ? MemberRosterParser.Senate : MemberRosterParser.House;
        return terms.Where(term => term.Chamber == rosterChamber && term.Start <= day && day <= term.End)
            .OrderByDescending(term => term.Start).FirstOrDefault();
    }
}
