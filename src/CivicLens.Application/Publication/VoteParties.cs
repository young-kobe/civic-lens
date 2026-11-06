using CivicLens.Application.Roster;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Finds the party of a roll call member on the day of the vote. The roster term of the vote chamber that covers the day comes first, and a caucus beats the party,
/// so an Independent who caucuses with a party counts with it. The roster holds current members only, so a member who left takes the party named by the letter on the member line.
/// A member with neither counts as Unknown. When two terms cover the day, the term that starts later wins.
/// The party of a vote day is a fixed fact. The first publication of a member list stores its parties, and <see cref="Pinned"/> gives those stored parties
/// the first say. The roster rule then applies only to a list with no stored parties.
/// </summary>
internal sealed class VoteParties
{
    private readonly Dictionary<string, RosterTerm[]> termsBySourceId;
    private readonly IReadOnlyDictionary<string, string> stored;

    public VoteParties(IEnumerable<RosterMember> roster)
    {
        termsBySourceId = new(StringComparer.Ordinal);
        foreach (var member in roster)
            termsBySourceId[member.BioguideId] = member.Terms;
        stored = new Dictionary<string, string>();
    }

    private VoteParties(Dictionary<string, RosterTerm[]> termsBySourceId, IReadOnlyDictionary<string, string> stored)
    {
        this.termsBySourceId = termsBySourceId;
        this.stored = stored;
    }

    /// <summary>Returns the same roster rule with the parties that the member list of one vote already stores. It shares the roster terms.</summary>
    public VoteParties Pinned(IReadOnlyDictionary<string, string> storedParties) => new(termsBySourceId, storedParties);

    public string PartyOn(VoteMemberLine line, Chamber chamber, DateOnly day) =>
        stored.TryGetValue(line.SourceId, out var pinned) ? pinned : RosterPartyOn(line, chamber, day);

    private string RosterPartyOn(VoteMemberLine line, Chamber chamber, DateOnly day) =>
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
