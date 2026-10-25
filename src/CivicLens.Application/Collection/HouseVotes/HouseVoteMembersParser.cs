namespace CivicLens.Application.Collection.HouseVotes;

public static class HouseVoteMembersParser
{
    public const string ExtractionVersion = "congress-house-vote-members-v1";

    public static HouseVoteMemberVotes Parse(ReadOnlySpan<byte> json, HouseVoteRoll expected)
    {
        var wire = CongressApiJson.Deserialize<WireMembers>(json, "House vote members");
        var members = wire.HouseRollCallVoteMemberVotes
            ?? throw new InvalidDataException("House vote members must contain houseRollCallVoteMemberVotes.");
        if (members.Results is not { Length: > 0 } results || results.Any(IsIncomplete))
            throw new InvalidDataException("House vote members must list members with a bioguide ID and a vote.");
        return new HouseVoteMemberVotes(members.ToVote(expected), [.. results.Select(ToMember)]);
    }

    private static bool IsIncomplete(WireMember? member) =>
        member is null || string.IsNullOrWhiteSpace(member.BioguideId) || string.IsNullOrWhiteSpace(member.VoteCast);

    private static HouseVoteMember ToMember(WireMember? member) =>
        new(member!.BioguideId!, member.FirstName, member.LastName, member.VoteParty, member.VoteState, member.VoteCast!);

    private sealed record WireMembers(WireMemberVotes? HouseRollCallVoteMemberVotes);

    private sealed record WireMemberVotes : HouseVoteWire
    {
        public WireMember?[]? Results { get; init; }
    }

    private sealed record WireMember(string? BioguideId, string? FirstName, string? LastName, string? VoteParty, string? VoteState,
        string? VoteCast);
}
