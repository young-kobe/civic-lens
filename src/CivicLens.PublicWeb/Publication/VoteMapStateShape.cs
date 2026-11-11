namespace CivicLens.PublicWeb.Publication;

/// <summary>The outline of one state and the box around it. The Senate map splits the box in two at its middle.</summary>
public sealed record VoteMapStateShape(string State, string Path, double Left, double Top, double Right, double Bottom)
{
    public double Middle => (Left + Right) / 2;
}
