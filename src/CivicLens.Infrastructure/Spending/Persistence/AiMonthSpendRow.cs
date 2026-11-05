namespace CivicLens.Infrastructure.Spending.Persistence;

internal sealed class AiMonthSpendRow
{
    public DateOnly MonthStart { get; set; }
    public long ChargedMicros { get; set; }
}
