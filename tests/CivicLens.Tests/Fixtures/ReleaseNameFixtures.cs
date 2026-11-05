using CivicLens.Application.Publication;

namespace CivicLens.Tests.Fixtures;

internal static class ReleaseNameFixtures
{
    public static ReleaseNames None { get; } = new([], new ReleaseFacets([], []));
}
