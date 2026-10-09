namespace CivicLens.Application.Publication;

public sealed class PublicationConflictException(string message) : InvalidOperationException(message);
