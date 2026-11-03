using System.Reflection;
using CivicLens.Application;
using CivicLens.Host.Collection;
using CivicLens.Host.Publication;

namespace CivicLens.Host;

public static class BuildCodeVersion
{
    public static CodeVersion ReadFor(string[] args, string? releaseDirectory, Assembly assembly)
    {
        var codeVersion = CodeVersion.FromAssembly(assembly);
        return WritesCodeRevisionsOrReleases(args, releaseDirectory) ? codeVersion.Require() : codeVersion;
    }

    private static bool WritesCodeRevisionsOrReleases(string[] args, string? releaseDirectory) =>
        CollectionWorkerCommand.Matches(args) || ReleaseCommand.IsPublish(args) || (args is ["review", ..] && releaseDirectory is not null);
}
