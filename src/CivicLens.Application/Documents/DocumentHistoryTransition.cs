using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed record DocumentHistoryTransition(
    string TextSha256,
    string Text,
    ImmutableArray<string> AttemptIds,
    ImmutableArray<string> ExtractionIds);
