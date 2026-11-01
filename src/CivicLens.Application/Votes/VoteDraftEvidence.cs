using CivicLens.Application.Collection;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftEvidence(VoteDraftCandidate Candidate, CollectionConfigurationRevision ConfigurationRevision,
    VoteDraftPage Detail, VoteDraftPage Members, VoteDraftPage? BillDetail, IReadOnlyList<VoteDraftPage> BillSubjects, VoteDraftRevision? Current, bool HasNewerCandidate,
    bool DraftedUnderOlderPrompt);
