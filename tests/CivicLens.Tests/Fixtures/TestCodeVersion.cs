using CivicLens.Application;

namespace CivicLens.Tests.Fixtures;

internal static class TestCodeVersion
{
    public static readonly string Sha = new('c', 40);

    public static readonly CodeVersion Current = CodeVersion.Of(Sha);
}
