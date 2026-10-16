namespace CivicLens.Application.Collection.HouseVotes;

public static class HouseVoteParser
{
    public const string Version = "congress-house-vote-v1";

    public static HouseVoteDetail Parse(ReadOnlySpan<byte> json, HouseVoteRoll expected)
    {
        var wire = HouseVoteJson.Deserialize<WireDetail>(json, "House vote");
        return (wire.HouseRollCallVote ?? throw new InvalidDataException("House vote must contain houseRollCallVote."))
            .ToDetail(expected);
    }

    private sealed record WireDetail(WireVote? HouseRollCallVote);

    private sealed record WireVote : HouseVoteWire
    {
        public DateTimeOffset? StartDate { get; init; }
        public string? VoteQuestion { get; init; }
        public string? VoteType { get; init; }
        public string? Result { get; init; }
        public string? SourceDataUrl { get; init; }
        public WirePartyTotal?[]? VotePartyTotal { get; init; }

        public HouseVoteDetail ToDetail(HouseVoteRoll expected) => new(ToVote(expected), StartDate, VoteQuestion, VoteType, Result,
            LegislationType, LegislationNumber, ReadPartyTotals(), SourceDataUrl);

        private HouseVotePartyTotal[] ReadPartyTotals() => [.. (VotePartyTotal ?? []).Select(ReadPartyTotal)];

        private static HouseVotePartyTotal ReadPartyTotal(WirePartyTotal? total) =>
            total is { VoteParty: { } party } && !string.IsNullOrWhiteSpace(party)
                ? new HouseVotePartyTotal(party, total.YeaTotal, total.NayTotal, total.PresentTotal, total.NotVotingTotal)
                : throw new InvalidDataException("House vote party totals must name a party.");
    }

    private sealed record WirePartyTotal(string? VoteParty, int? YeaTotal, int? NayTotal, int? PresentTotal, int? NotVotingTotal);
}
