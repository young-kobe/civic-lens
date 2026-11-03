using CivicLens.Core.Actions;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionSponsorshipRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string PersonId { get; set; } = string.Empty;
    public string BioguideId { get; set; } = string.Empty;
    public ActionSponsorshipRole Role { get; set; }
    public bool IsOriginal { get; set; }
    public DateOnly SponsoredOn { get; set; }
    public DateOnly? WithdrawnOn { get; set; }
    public List<ActionRevisionSponsorshipCitationRow> Citations { get; set; } = [];
}
