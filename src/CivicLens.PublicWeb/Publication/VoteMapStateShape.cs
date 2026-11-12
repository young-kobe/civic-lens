namespace CivicLens.PublicWeb.Publication;

/// <summary>The outline of one state and the box around it. The Senate map splits the state in two at <paramref name="Middle"/>, the x that halves its area.</summary>
public sealed record VoteMapStateShape(string State, string Path, double Left, double Top, double Right, double Bottom, double Middle);
