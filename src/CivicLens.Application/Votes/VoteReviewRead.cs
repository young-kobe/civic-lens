using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteReviewRead(VoteReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions,
    ImmutableDictionary<string, string> PersonNames);
