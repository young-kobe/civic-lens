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
        public WireTotal?[]? VotePartyTotal { get; init; }

        public HouseVoteDetail ToDetail(HouseVoteRoll expected)
        {
            var totals = (VotePartyTotal ?? []).Select(ReadTotal).ToArray();
            return new(ToVote(expected), StartDate, VoteQuestion, VoteType, Result, LegislationType, LegislationNumber,
                [.. totals.Where(total => total.IsParty).Select(total => total.ToPartyTotal())],
                [.. totals.Where(total => !total.IsParty).Select(total => total.ToCandidateTotal())], SourceDataUrl);
        }

        private static WireTotal ReadTotal(WireTotal? total)
        {
            if (total is null) throw new InvalidDataException("House vote totals must be objects.");
            if (total.IsParty == total.IsCandidate)
                throw new InvalidDataException("House vote totals must name a party or a candidate, not both or neither.");
            if (total.IsCandidate && total.Total is not >= 0)
                throw new InvalidDataException("House vote candidate totals must have a total of 0 or more.");
            return total;
        }
    }

    private sealed record WireTotal(string? VoteParty, int? YeaTotal, int? NayTotal, int? PresentTotal, int? NotVotingTotal,
        string? Candidate, int? Total)
    {
        public bool IsParty => !string.IsNullOrWhiteSpace(VoteParty);

        public bool IsCandidate => !string.IsNullOrWhiteSpace(Candidate);

        public HouseVotePartyTotal ToPartyTotal() => new(VoteParty!, YeaTotal, NayTotal, PresentTotal, NotVotingTotal);

        public HouseVoteCandidateTotal ToCandidateTotal() => new(Candidate!, Total!.Value);
    }
}
