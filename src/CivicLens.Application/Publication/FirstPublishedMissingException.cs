namespace CivicLens.Application.Publication;

public sealed class FirstPublishedMissingException(string message) : InvalidOperationException(message);
