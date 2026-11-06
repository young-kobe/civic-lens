using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

/// <param name="Review">The vote review.</param>
/// <param name="Extractions">The extractions that the current revision cites.</param>
public sealed record PublishableVote(VoteReview Review, ImmutableDictionary<string, DocumentExtraction> Extractions)
{
    /// <summary>
    /// The party of each member on the vote day, by Bioguide ID, when an earlier publication already stored them on the shared member list of the current revision.
    /// It is empty when the list holds no stored parties.
    /// </summary>
    public ImmutableDictionary<string, string> StoredParties { get; init; } = ImmutableDictionary<string, string>.Empty;
}
