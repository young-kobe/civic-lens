namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class OfficialPhotoRow
{
    public string AttemptId { get; set; } = null!;
    public string PersonId { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;
    public string Credit { get; set; } = null!;
}
