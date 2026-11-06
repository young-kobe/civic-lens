using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

/// <summary>The current revision of an action with the extractions that it cites. TextVersion is the stored row of the bill text that a text draft cites, and null for any other revision.</summary>
public sealed record PublishableAction(ActionReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions, ActionTextVersion? TextVersion);
