using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed record DocumentHistory(
    ImmutableArray<DocumentHistoryObservation> Observations,
    ImmutableArray<DocumentHistoryStream> Streams);
