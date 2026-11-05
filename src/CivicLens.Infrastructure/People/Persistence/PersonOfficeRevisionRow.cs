namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonOfficeRevisionRow
{
    public string PersonId { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public string Note { get; set; } = null!;
    public string ActorSubject { get; set; } = null!;
    public long RecordedAtUtcTicks { get; set; }
    public long RegistrySequence { get; set; }
    public List<PersonOfficeRevisionTermRow> Terms { get; set; } = [];
}
