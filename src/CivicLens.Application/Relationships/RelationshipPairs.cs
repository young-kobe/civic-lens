using System.Collections.Immutable;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipPairs(ImmutableArray<RelationshipPair> Found, int StatementsSkippedWithoutDate);
