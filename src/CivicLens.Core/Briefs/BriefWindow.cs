namespace CivicLens.Core.Briefs;

public sealed record BriefWindow(DateOnly WeekStart, long StartUtcTicks, long EndUtcTicks, int FirstReleaseNumber, int CutoffReleaseNumber)
{
    public const long ShortestWeekTicks = 167 * TimeSpan.TicksPerHour;
    public const long LongestWeekTicks = 169 * TimeSpan.TicksPerHour;

    public string RecordId => BriefWeek.RecordIdOf(WeekStart);

    public void Validate()
    {
        BriefWeek.RequireMonday(WeekStart);
        var length = EndUtcTicks - StartUtcTicks;
        if (StartUtcTicks < 0 || length < ShortestWeekTicks || length > LongestWeekTicks)
            throw new ArgumentException("A brief week lasts 167 to 169 hours, because of the clock change.");
        if (FirstReleaseNumber < 1 || FirstReleaseNumber > CutoffReleaseNumber)
            throw new ArgumentException("A brief needs a first release that does not follow its cutoff release.");
    }
}
