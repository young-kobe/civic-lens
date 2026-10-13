namespace CivicLens.Application.Collection.Discovery;

public sealed class DiscoveryAdmissionBudget
{
    private readonly DiscoveryAdmissionPolicy policy;
    private readonly int jobRequests;
    private readonly long jobBytes;
    private readonly int jobSeconds;
    private int admittedJobs;
    private int reservedRequests;
    private long reservedBytes;
    private int reservedSeconds;

    internal DiscoveryAdmissionBudget(DiscoveryAdmissionPolicy policy, int jobRequests, long jobBytes, int jobSeconds)
    {
        this.policy = policy;
        this.jobRequests = jobRequests;
        this.jobBytes = jobBytes;
        this.jobSeconds = jobSeconds;
    }

    public bool TryReserve()
    {
        if (admittedJobs >= policy.MaxJobs || jobRequests > policy.MaxTotalRequests - reservedRequests ||
            jobBytes > policy.MaxTotalBytes - reservedBytes || jobSeconds > policy.MaxTotalTimeoutSeconds - reservedSeconds)
            return false;

        admittedJobs++;
        reservedRequests = checked(reservedRequests + jobRequests);
        reservedBytes = checked(reservedBytes + jobBytes);
        reservedSeconds = checked(reservedSeconds + jobSeconds);
        return true;
    }
}
