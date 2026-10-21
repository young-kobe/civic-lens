namespace CivicLens.Infrastructure.Reports.Persistence;

internal sealed class ReaderReportCursorRow
{
    public const int OnlyId = 1;

    public int Id { get; set; }
    public string Cursor { get; set; } = "";
}
