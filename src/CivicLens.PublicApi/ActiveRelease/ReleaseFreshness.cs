using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.ActiveRelease;

/// <summary>The active release and the newest published roll call day of each chamber. A chamber with no published roll call has no day.</summary>
public sealed record ReleaseFreshness(int ReleaseNumber, DateOnly? NewestHouseRollCall, DateOnly? NewestSenateRollCall)
{
    /// <summary>The context that the shell of a server page renders its freshness line from.</summary>
    public ServerPageContext PageContext => new(NewestHouseRollCall, NewestSenateRollCall);
}
