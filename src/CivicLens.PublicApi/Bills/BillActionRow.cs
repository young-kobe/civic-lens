namespace CivicLens.PublicApi.Bills;

public sealed record BillActionRow
{
    public required int Position { get; init; }
    public required DateOnly ActionDate { get; init; }
    public TimeOnly? ActionTime { get; init; }
    public required string Text { get; init; }
    public string? Type { get; init; }
    public string? ActionCode { get; init; }
    public int? SourceSystemCode { get; init; }
    public string? SourceSystemName { get; init; }
}
