using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One tracked member on one roll call. The party is the group that the party counts use for the member. The state and the district come from the same term as the party. They are null when the release has no such term.</summary>
public sealed record VoteMemberData(string OfficialId, string Name, string Cast, string? Party, PublishedCitation Citation, string? State = null, int? District = null)
{
    /// <summary>The party words of the member, or the no-party words when the release has no party for the term.</summary>
    public string PartyLabel => Party ?? VoteWords.NoKnownParty;
}
