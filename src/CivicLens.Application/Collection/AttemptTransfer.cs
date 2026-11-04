namespace CivicLens.Application.Collection;

/// <summary>What one collection attempt measured on the wire.</summary>
public sealed record AttemptTransfer(int RequestCount, long BytesReceived);
