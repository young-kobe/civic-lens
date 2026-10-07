using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed record DocumentHistoryObservation(
    CivicLens.Application.Collection.StoredCollectionAttempt Attempt,
    ImmutableArray<DocumentExtraction> Extractions);
