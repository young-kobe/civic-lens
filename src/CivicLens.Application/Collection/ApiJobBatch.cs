using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection;

public sealed record ApiJobBatch(
    DiscoveryAdmissionPolicy Policy,
    CollectionJobDefinition Template,
    IReadOnlyList<ApiJob> Jobs);
