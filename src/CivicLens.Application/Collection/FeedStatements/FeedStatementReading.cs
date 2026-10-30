using CivicLens.Application.Documents;

namespace CivicLens.Application.Collection.FeedStatements;

public sealed record FeedStatementReading(IReadOnlyList<FeedItemExtraction> Items, int UnreadableItems);
