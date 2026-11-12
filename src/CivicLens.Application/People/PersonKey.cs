namespace CivicLens.Application.People;

/// <summary>The person ID of one people row and its bioguide ID, when the row has one.</summary>
public sealed record PersonKey(string PersonId, string? BioguideId);
