using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

public sealed record PublishableAction(ActionReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions);
