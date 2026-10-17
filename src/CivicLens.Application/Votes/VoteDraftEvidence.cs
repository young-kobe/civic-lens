using CivicLens.Application.Collection;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftEvidence(VoteDraftCandidate Candidate, CollectionConfigurationRevision ConfigurationRevision,
    VoteDraftPage Detail, VoteDraftPage Members, VoteDraftPage? BillDetail, VoteDraftRevision? Current, bool HasNewerCandidate);
