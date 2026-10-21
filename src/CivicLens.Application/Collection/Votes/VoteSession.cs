namespace CivicLens.Application.Collection.Votes;

public readonly record struct VoteSession(int Congress, int Number)
{
    private const int FirstCongressYear = 1787;

    public int Year => FirstCongressYear + 2 * Congress + Number - 1;

    public static VoteSession ForYear(int year) =>
        new((year - FirstCongressYear) / 2, year % 2 == 1 ? 1 : 2);

    public static VoteSession? TryCreate(int congress, int number) =>
        congress >= 1 && number is 1 or 2 ? new VoteSession(congress, number) : null;

    public static VoteSession? TryRead(string congress, string number) =>
        ApiPageQuery.TryReadNumber(congress) is { } parsedCongress && ApiPageQuery.TryReadNumber(number) is { } parsedNumber
            ? TryCreate(parsedCongress, parsedNumber)
            : null;

    public static VoteSession? FirstOfSweep(DateOnly backfillFrom, DateOnly today, int minimumCongress)
    {
        var first = ForYear(backfillFrom.Year);
        if (first.Congress < minimumCongress) first = new VoteSession(minimumCongress, 1);
        return first.Year <= today.Year ? first : null;
    }

    public VoteSession? Following(DateOnly today) => Year < today.Year ? ForYear(Year + 1) : null;
}
