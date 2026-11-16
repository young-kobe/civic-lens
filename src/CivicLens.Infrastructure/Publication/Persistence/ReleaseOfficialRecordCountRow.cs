namespace CivicLens.Infrastructure.Publication.Persistence;

/// <summary>The record counts of one official in one release: every record that names the official, and each tab of the official record page.</summary>
internal sealed class ReleaseOfficialRecordCountRow
{
    public int ReleaseNumber { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public int Records { get; set; }
    public int Votes { get; set; }
    public int Bills { get; set; }
    public int Statements { get; set; }
    public int ExecutiveActions { get; set; }
}
