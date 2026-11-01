using CivicLens.Application.Publication;

namespace CivicLens.Host.Review;

public sealed record TakeOffSiteView(bool IsOwner, bool Configured, bool Failed, TakeOffSiteStatus Status)
{
    public static TakeOffSiteView Hidden { get; } = new(false, false, false, TakeOffSiteStatus.NotPublished);
}
