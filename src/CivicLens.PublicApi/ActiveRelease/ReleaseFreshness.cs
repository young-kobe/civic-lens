using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.ActiveRelease;

/// <summary>The active release and the newest published roll call day of each chamber. A chamber with no published roll call has no day.</summary>
public sealed record ReleaseFreshness(int ReleaseNumber, DateOnly? NewestHouseRollCall, DateOnly? NewestSenateRollCall)
{
    /// <summary>The context that the shell of a server page renders its freshness line from.</summary>
    public ServerPageContext PageContext => new(NewestHouseRollCall, NewestSenateRollCall);

    /// <summary>The context for a page of one release. When the days belong to another release, the page shows no line.</summary>
    public ServerPageContext PageContextFor(int pageRelease) => ReleaseNumber == pageRelease ? PageContext : new(null, null);
}
