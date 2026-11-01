namespace CivicLens.Application.Publication;

public sealed class PublicationStateMismatchException(string message) : InvalidOperationException(message);
