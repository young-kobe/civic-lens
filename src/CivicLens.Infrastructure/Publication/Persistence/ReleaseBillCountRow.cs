namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseBillCountRow
{
    public int ReleaseNumber { get; set; }
    public int Congress { get; set; }
    public int Bills { get; set; }
    public int ExpectedMemberRowCount { get; set; }
}
