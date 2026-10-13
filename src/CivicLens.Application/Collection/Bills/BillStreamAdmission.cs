using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStreamAdmission(
    string SourceId,
    DiscoveryAdmissionPolicy Policy,
    CollectionJobDefinition Template,
    BillStreamWindow Window,
    IReadOnlyList<BillStreamJob> Bills,
    BillStreamJob? NextPage)
{
    public bool EndsWindow => NextPage is null;
}
