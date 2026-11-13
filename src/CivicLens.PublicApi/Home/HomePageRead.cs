using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Home;

/// <summary>The home page data and the freshness of the same home read, so the shell shows the newest roll call days without a second read.</summary>
public sealed record HomePageRead(HomePageData Data, ReleaseFreshness Freshness);
