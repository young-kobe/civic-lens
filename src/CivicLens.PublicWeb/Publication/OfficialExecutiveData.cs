namespace CivicLens.PublicWeb.Publication;

/// <summary>The executive actions of an official, from the first signing date to the last. <c>Total</c> counts all types.</summary>
public sealed record OfficialExecutiveData(int Total, DateOnly FirstSignedOn, DateOnly LastSignedOn, OfficialExecutiveType[] Types);
