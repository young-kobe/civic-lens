namespace CivicLens.Application.Review;

public sealed record RevisionVerification(int Checked, int Legacy, int Mismatched, IReadOnlyList<RevisionMismatch> FirstMismatches);
