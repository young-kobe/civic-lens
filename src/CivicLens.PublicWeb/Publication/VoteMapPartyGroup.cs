using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One party in the party color mode legend: how its members cast on the map, with seats and delegate circles together. A null party is a member with no party in the release.</summary>
public sealed record VoteMapPartyGroup(string? Party, IReadOnlyList<(VoteMapMark Mark, int Count)> Casts)
{
    public string Label => Party is null ? VoteWords.NoKnownParty : PublishedPartyNames.Members(Party);

    public PartyTone Tone => Party is null ? PartyTone.Neutral : PartyTones.Of(Party);
}
