namespace CivicLens.PublicApi.Maps;

/// <summary>The selected map with the release-day Congress and all issue choices from the same snapshot.</summary>
public sealed record MapPageAnswer(MapAnswer Map, int? ReleaseCongress, MapIssueAnswer[] IssueChoices);
