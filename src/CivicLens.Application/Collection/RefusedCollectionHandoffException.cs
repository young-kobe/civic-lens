namespace CivicLens.Application.Collection;

/// <summary>A saved handoff that this version can never import, because it has an old version or no elapsed time.</summary>
public sealed class RefusedCollectionHandoffException(string message) : Exception(message);
