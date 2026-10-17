using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record PublishableVote(VoteReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions,
    ImmutableArray<string> BillActionRecordIds);
