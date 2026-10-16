namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefScheduleRow
{
    public string Task { get; set; } = string.Empty;
    public DateOnly FirstWeekStart { get; set; }
}
