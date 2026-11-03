using System.Collections.Immutable;

namespace CivicLens.Application.Review;

public sealed record RevisionBackfillBatch(int Examined, ImmutableArray<SkippedRevision> Skipped, RevisionKey? Last);
