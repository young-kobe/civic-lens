using CivicLens.Core.Documents;

namespace CivicLens.Core.Relationships;

public sealed record RelationshipMatch(RelationshipMatchBasis Basis, SourceSpan Span);
