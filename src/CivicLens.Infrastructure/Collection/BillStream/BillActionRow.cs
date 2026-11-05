namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillActionRow
{
    public string DetailJobId { get; set; } = "";
    public int Position { get; set; }
    public string JobId { get; set; } = "";
    public DateOnly ActionDate { get; set; }
    public TimeOnly? ActionTime { get; set; }
    public string Text { get; set; } = "";
    public string? Type { get; set; }
    public string? ActionCode { get; set; }
    public int? SourceSystemCode { get; set; }
    public string? SourceSystemName { get; set; }
}
