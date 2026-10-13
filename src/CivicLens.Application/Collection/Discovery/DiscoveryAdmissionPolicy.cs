using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Application.Collection.Discovery;

public sealed record DiscoveryAdmissionPolicy
{
    public int MaxJobs { get; init; } = 10;
    public int MaxTotalRequests { get; init; } = 150;
    public long MaxTotalBytes { get; init; } = 60_000_000;
    public int MaxTotalTimeoutSeconds { get; init; } = 900;

    public void Validate()
    {
        if (MaxJobs is < 1 or > 1000 || MaxTotalRequests is < 1 or > 20_000 ||
            MaxTotalBytes is < 1 or > 10_000_000_000 || MaxTotalTimeoutSeconds is < 1 or > 30_000)
            throw new ArgumentException("Discovery admission limits are outside supported bounds.");
    }

    public DiscoveryAdmissionBudget OpenBudget(CollectionJobDefinition template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var request = template.CreateRequest("discovery-admission-validation", Path.GetFullPath("collection-artifacts"));
        return new DiscoveryAdmissionBudget(this, template.Policy.ResolveMaxTotalRequests(request),
            template.Policy.ResolveMaxTotalBytes(request), template.Policy.ResolveMaxTotalTimeoutSeconds(request));
    }

    public bool CanReserve(CollectionJobDefinition template, int jobs)
    {
        var budget = OpenBudget(template);
        for (var reserved = 0; reserved < jobs; reserved++)
            if (!budget.TryReserve())
                return false;
        return true;
    }
}
