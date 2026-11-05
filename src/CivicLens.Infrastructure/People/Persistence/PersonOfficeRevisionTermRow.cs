using CivicLens.Application.Collection;

namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonOfficeRevisionTermRow
{
    public string PersonId { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public int Position { get; set; }
    public string Title { get; set; } = null!;
    public ConfiguredOfficeKind Kind { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public string? Party { get; set; }
}
