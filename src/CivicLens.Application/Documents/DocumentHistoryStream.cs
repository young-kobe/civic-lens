using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed record DocumentHistoryStream(
    string ParserVersion,
    string NormalizationVersion,
    string? ProfileRevisionId,
    ImmutableArray<DocumentHistoryTransition> Transitions);
