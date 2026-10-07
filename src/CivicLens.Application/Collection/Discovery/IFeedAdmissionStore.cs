namespace CivicLens.Application.Collection.Discovery;

public interface IFeedAdmissionStore
{
    Task<FeedAdmissionResult> AdmitAsync(FeedAdmissionRequest request, CancellationToken cancellationToken);
}
