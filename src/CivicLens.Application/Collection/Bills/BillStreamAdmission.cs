using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.Bills;

public sealed record BillStreamAdmission(
    string SourceId,
    DiscoveryAdmissionPolicy Policy,
    BillStreamWindow Window,
    IReadOnlyList<BillStreamJob> Jobs,
    bool EndsWindow);
