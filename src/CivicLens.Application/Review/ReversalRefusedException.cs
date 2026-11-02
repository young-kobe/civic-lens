namespace CivicLens.Application.Review;

public sealed class ReversalRefusedException() : InvalidOperationException(
    "A newer update of this record arrived while it was rejected, so approving it now would publish old facts. Request changes first. Then ask the owner to requeue the newer update.");
