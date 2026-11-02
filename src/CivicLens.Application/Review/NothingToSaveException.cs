namespace CivicLens.Application.Review;

public sealed class NothingToSaveException() : InvalidOperationException("Nothing to save. This revision is already approved.");
