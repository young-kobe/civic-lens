using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

public sealed record ActionReviewRead(ActionReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions,
    ImmutableDictionary<string, string> OfficialNames, ImmutableArray<BillVoteDraft> BillVotes);
