namespace CivicLens.Application.Collection.HouseVotes;

public readonly record struct HouseVoteSession(int Congress, int Number)
{
    private const int FirstCongressYear = 1787;

    public int Year => FirstCongressYear + 2 * Congress + Number - 1;

    public static HouseVoteSession ForYear(int year) =>
        new((year - FirstCongressYear) / 2, year % 2 == 1 ? 1 : 2);

    public static HouseVoteSession? TryCreate(int congress, int number) =>
        congress >= 1 && number is 1 or 2 ? new HouseVoteSession(congress, number) : null;

    public static HouseVoteSession? TryRead(string congress, string number) =>
        ApiPageQuery.TryReadNumber(congress) is { } parsedCongress && ApiPageQuery.TryReadNumber(number) is { } parsedNumber
            ? TryCreate(parsedCongress, parsedNumber)
            : null;

    public static HouseVoteSession? FirstOfSweep(DateOnly backfillFrom, DateOnly today, int minimumCongress)
    {
        var first = ForYear(backfillFrom.Year);
        if (first.Congress < minimumCongress) first = new HouseVoteSession(minimumCongress, 1);
        return first.Year <= today.Year ? first : null;
    }

    public HouseVoteSession? Following(DateOnly today) => Year < today.Year ? ForYear(Year + 1) : null;
}
