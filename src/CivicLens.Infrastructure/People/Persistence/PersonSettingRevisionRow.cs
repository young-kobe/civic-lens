namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonSettingRevisionRow
{
    public string PersonId { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public bool WithholdPhoto { get; set; }
    public string? Website { get; set; }
    public string Note { get; set; } = null!;
    public string ActorSubject { get; set; } = null!;
    public long RecordedAtUtcTicks { get; set; }
    public long RegistrySequence { get; set; }
}
