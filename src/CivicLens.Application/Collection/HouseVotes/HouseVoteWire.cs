using CivicLens.Application.Collection.Bills;

namespace CivicLens.Application.Collection.HouseVotes;

internal record HouseVoteWire
{
    public int? Congress { get; init; }
    public int? SessionNumber { get; init; }
    public int? RollCallNumber { get; init; }
    public DateTimeOffset? UpdateDate { get; init; }
    public string? LegislationType { get; init; }
    public string? LegislationNumber { get; init; }

    public HouseVote ToVote(HouseVoteRoll expected)
    {
        if (UpdateDate is null || ReadRoll() != expected)
            throw new InvalidDataException("House vote is for another roll call than requested or has no update date.");
        var (billType, billNumber) = ReadBill();
        return new HouseVote(expected, UpdateDate.Value, billType, billNumber);
    }

    internal HouseVoteRoll? ReadRoll() =>
        Congress is { } congress && SessionNumber is { } session && RollCallNumber is >= 1 and var number &&
        HouseVoteSession.TryCreate(congress, session) is { } voteSession
            ? new HouseVoteRoll(voteSession, number)
            : null;

    private (string? Type, int? Number) ReadBill()
    {
        var hasType = !string.IsNullOrWhiteSpace(LegislationType);
        var hasNumber = !string.IsNullOrWhiteSpace(LegislationNumber);
        if (!hasType && !hasNumber) return (null, null);
        if (!hasType) throw new InvalidDataException("House vote legislation needs a type and a number together.");
        var type = CongressBillListItem.NormalizeType(LegislationType!);
        if (type is null) return (null, null);
        var number = hasNumber ? ApiPageQuery.TryReadNumber(LegislationNumber!) : null;
        if (number is not >= 1)
            throw new InvalidDataException("House vote legislation needs a type and a number together.");
        return (type, number);
    }
}
