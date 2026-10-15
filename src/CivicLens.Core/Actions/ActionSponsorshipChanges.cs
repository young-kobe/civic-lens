using System.Collections.Immutable;

namespace CivicLens.Core.Actions;

public sealed record ActionSponsorshipChanges(ImmutableArray<ActionSponsorship> Added, ImmutableArray<ActionSponsorship> Changed,
    ImmutableArray<ActionSponsorship> Removed)
{
    public bool IsEmpty => Added.IsEmpty && Changed.IsEmpty && Removed.IsEmpty;
}
