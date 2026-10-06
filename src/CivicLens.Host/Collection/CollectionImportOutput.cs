using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Collection;

internal sealed record CollectionImportOutput(string AttemptId, CollectionOutcome Outcome,
    string ImportDisposition, string PriorCaptureLinkStatus, bool HandoffRemoved);
