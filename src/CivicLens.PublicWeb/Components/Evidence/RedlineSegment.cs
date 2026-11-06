namespace CivicLens.PublicWeb.Components.Evidence;

public enum RedlineKind
{
    Context,
    Same,
    Removed,
    Added
}

public sealed record RedlineSegment(RedlineKind Kind, string Text);
