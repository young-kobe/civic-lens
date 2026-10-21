using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly string Root = RepositoryRoot.FullName;

    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["CivicLens.Core"] = [],
        ["CivicLens.Collection.Contracts"] = [],
        ["CivicLens.Publication.Contracts"] = [],
        ["CivicLens.Application"] = ["CivicLens.Core", "CivicLens.Collection.Contracts", "CivicLens.Publication.Contracts"],
        ["CivicLens.Infrastructure"] = ["CivicLens.Application"],
        ["CivicLens.Host"] = ["CivicLens.Application", "CivicLens.Infrastructure"],
        ["CivicLens.Collector"] = ["CivicLens.Collection.Contracts"],
        ["CivicLens.PublicApi"] = ["CivicLens.Publication.Contracts"]
    };

    [Fact]
    public void ProductionProjectsHaveOnlyApprovedDependencies()
    {
        var projects = Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.Equal(Allowed.Keys.Order(), projects.Select(Path.GetFileNameWithoutExtension).Order());

        foreach (var path in projects)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var xml = XDocument.Load(path);
            Assert.Equal(name is "CivicLens.Host" or "CivicLens.PublicApi" ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk",
                xml.Root!.Attribute("Sdk")!.Value);
            Assert.Empty(xml.Descendants("FrameworkReference")); // Host and PublicApi alone receive ASP.NET through the Web SDK.
            var references = xml.Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value));
            Assert.Equal(Allowed[name].Order(), references.Order());
            Assert.Empty(xml.Descendants("Reference")); // No binary references bypassing the project graph.

            string[] allowedPackages = name == "CivicLens.Infrastructure"
                ? ["AngleSharp", "Anthropic", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Relational", "Npgsql.EntityFrameworkCore.PostgreSQL"]
                : name == "CivicLens.Application" ? ["Microsoft.Extensions.Logging.Abstractions"]
                : name == "CivicLens.Collector" ? ["AngleSharp"]
                : name == "CivicLens.Host" ? ["Microsoft.AspNetCore.Authentication.OpenIdConnect"]
                : name == "CivicLens.PublicApi" ? ["Npgsql"] : [];
            Assert.Equal(allowedPackages.Order(), xml.Descendants("PackageReference")
                .Select(element => element.Attribute("Include")!.Value).Order());
        }
    }

    [Fact]
    public void OnlyInfrastructureReferencesTheModelSdkAssembly()
    {
        var referencing = Directory.GetFiles(AppContext.BaseDirectory, "CivicLens.*.dll")
            .Where(path => !Path.GetFileName(path).StartsWith("CivicLens.Tests", StringComparison.Ordinal))
            .Where(path => ReferencedAssemblies(path).Contains("Anthropic"))
            .Select(path => Path.GetFileNameWithoutExtension(path)!).ToArray();

        Assert.Equal(["CivicLens.Infrastructure"], referencing);
    }

    [Fact]
    public void ThePublicApiReadsReleasesOnlyAndReferencesNoOperationalAssembly()
    {
        var references = ReferencedAssemblies(Path.Combine(AppContext.BaseDirectory, "CivicLens.PublicApi.dll"));

        Assert.Equal(["CivicLens.Publication.Contracts"], references.Where(name => name.StartsWith("CivicLens.", StringComparison.Ordinal)));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    private static HashSet<string> ReferencedAssemblies(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        return [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
    }

    [Fact]
    public void SharedBuildConfigurationCannotInjectDependencies()
    {
        foreach (var path in Directory.GetFiles(Root, "Directory.Build.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var xml = XDocument.Load(path);
            Assert.Empty(xml.Descendants("ProjectReference"));
            Assert.Empty(xml.Descendants("PackageReference"));
            Assert.Empty(xml.Descendants("Reference"));
        }
    }
}
