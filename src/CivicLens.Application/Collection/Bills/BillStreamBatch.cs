using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStreamBatch(
    DiscoveryAdmissionPolicy Policy,
    CollectionJobDefinition Template,
    IReadOnlyList<BillStreamJob> Jobs);
