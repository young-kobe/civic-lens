namespace CivicLens.Application.Publication;

public sealed class ReleaseRewriteException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
