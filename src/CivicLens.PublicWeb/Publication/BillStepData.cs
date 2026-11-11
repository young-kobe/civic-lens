namespace CivicLens.PublicWeb.Publication;

/// <summary>One reached step of a bill. The title is a fixed approved phrase. The detail holds the date and the facts that the step names.</summary>
public sealed record BillStepData(string Title, string Detail);
