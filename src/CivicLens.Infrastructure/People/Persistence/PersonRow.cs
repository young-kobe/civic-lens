namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonRow
{
    public string PersonId { get; set; } = null!;
    public string? BioguideId { get; set; }
    public string? WikidataId { get; set; }
    public int? GovtrackId { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public string? CreatedByAttemptId { get; set; }
    public string? CreatedByActor { get; set; }
}
