namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class RosterTermRow
{
    public string AttemptId { get; set; } = null!;
    public string BioguideId { get; set; } = null!;
    public int TermIndex { get; set; }
    public string Chamber { get; set; } = null!;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string State { get; set; } = null!;
    public int? District { get; set; }
    public int? SenateClass { get; set; }
    public string Party { get; set; } = null!;
    public string? Caucus { get; set; }
}
