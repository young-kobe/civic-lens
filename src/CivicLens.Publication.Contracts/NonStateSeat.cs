namespace CivicLens.Publication.Contracts;

/// <summary>A place that sends a non-voting member to the House and no senators.</summary>
public sealed record NonStateSeat(string Code, string Name, string HouseOffice);
