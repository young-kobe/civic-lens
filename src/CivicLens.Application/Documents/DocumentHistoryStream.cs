using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed record DocumentHistoryStream(
    string ExtractionVersion,
    string? ProfileRevisionId,
    ImmutableArray<DocumentHistoryTransition> Transitions);
