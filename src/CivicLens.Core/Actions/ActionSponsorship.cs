using System.Collections.Immutable;
using CivicLens.Core.Statements;

namespace CivicLens.Core.Actions;

public sealed record ActionSponsorship(string PersonId, string BioguideId, ActionSponsorshipRole Role, bool IsOriginal,
    DateOnly SponsoredOn, DateOnly? WithdrawnOn, ImmutableArray<StatementSpan> Citations)
{
    public bool Equals(ActionSponsorship? other) =>
        other is not null && PersonId == other.PersonId && BioguideId == other.BioguideId && Role == other.Role &&
        IsOriginal == other.IsOriginal && SponsoredOn == other.SponsoredOn && WithdrawnOn == other.WithdrawnOn &&
        Citations.SequenceEqual(other.Citations);

    public override int GetHashCode() => HashCode.Combine(PersonId, BioguideId, Role, SponsoredOn, WithdrawnOn);

    public static (string BioguideId, ActionSponsorshipRole Role, DateOnly SponsoredOn) KeyOf(ActionSponsorship sponsorship) =>
        (sponsorship.BioguideId, sponsorship.Role, sponsorship.SponsoredOn);
}
