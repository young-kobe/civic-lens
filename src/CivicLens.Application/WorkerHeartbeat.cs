namespace CivicLens.Application;

/// <summary>The age is measured on the database clock.</summary>
public sealed record WorkerHeartbeat(string Loop, TimeSpan Age);
