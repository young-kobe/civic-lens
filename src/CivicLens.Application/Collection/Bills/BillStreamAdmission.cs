using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStreamAdmission(
    string SourceId,
    DiscoveryAdmissionPolicy Policy,
    CollectionJobDefinition Template,
    BillStreamWindow Window,
    bool StartsPass,
    IReadOnlyList<BillStreamJob> Bills,
    BillStreamJob? NextPage)
{
    public bool EndsWindow => NextPage is null;

    public BillStreamBatch Batch => new(Policy, Template, NextPage is null ? Bills : [.. Bills, NextPage]);
}
