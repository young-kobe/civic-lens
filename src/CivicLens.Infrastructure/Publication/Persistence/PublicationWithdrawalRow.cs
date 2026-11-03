namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationWithdrawalRow
{
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int WithdrawnRevisionNumber { get; set; }
    public long WithdrawnAtUtcTicks { get; set; }
    public string Note { get; set; } = string.Empty;
    public int FirstRelease { get; set; }
    public int? LastRelease { get; set; }
}
