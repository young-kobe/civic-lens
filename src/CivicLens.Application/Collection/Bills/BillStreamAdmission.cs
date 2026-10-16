using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStreamAdmission(
    string SourceId,
    DiscoveryAdmissionPolicy Policy,
    CollectionJobDefinition Template,
    BillStreamWindow Window,
    bool StartsPass,
    IReadOnlyList<ApiJob> Bills,
    ApiJob? NextPage)
{
    public bool EndsWindow => NextPage is null;

    public ApiJobBatch Batch => new(Policy, Template, NextPage is null ? Bills : [.. Bills, NextPage]);
}
