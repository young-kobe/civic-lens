namespace CivicLens.Infrastructure.Spending.Persistence;

internal sealed class AiMonthlyCapRevisionRow
{
    public int RevisionNumber { get; set; }
    public long CapMicros { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
}
