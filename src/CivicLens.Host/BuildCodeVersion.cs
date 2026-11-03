using System.Reflection;
using CivicLens.Application;
using CivicLens.Host.Collection;

namespace CivicLens.Host;

public static class BuildCodeVersion
{
    public static CodeVersion ReadFor(string[] args, Assembly assembly)
    {
        var codeVersion = CodeVersion.FromAssembly(assembly);
        return WritesCodeRevisionsOrReleases(args) ? codeVersion.Require() : codeVersion;
    }

    private static bool WritesCodeRevisionsOrReleases(string[] args) =>
        CollectionWorkerCommand.Matches(args) || args is ["releases", "publish", ..];
}
