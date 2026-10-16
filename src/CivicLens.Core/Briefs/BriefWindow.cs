namespace CivicLens.Core.Briefs;

public sealed record BriefWindow(DateOnly WeekStart, long StartUtcTicks, long EndUtcTicks, int FirstReleaseNumber, int CutoffReleaseNumber)
{
    private static readonly TimeSpan ShortestWeek = TimeSpan.FromHours(167);
    private static readonly TimeSpan LongestWeek = TimeSpan.FromHours(169);

    public string RecordId => BriefWeek.RecordIdOf(WeekStart);

    public void Validate()
    {
        BriefWeek.RequireMonday(WeekStart);
        var length = TimeSpan.FromTicks(EndUtcTicks - StartUtcTicks);
        if (StartUtcTicks < 0 || length < ShortestWeek || length > LongestWeek)
            throw new ArgumentException("A brief week lasts 167 to 169 hours, because of the clock change.");
        if (FirstReleaseNumber < 1 || FirstReleaseNumber > CutoffReleaseNumber)
            throw new ArgumentException("A brief needs a first release that does not follow its cutoff release.");
    }
}
