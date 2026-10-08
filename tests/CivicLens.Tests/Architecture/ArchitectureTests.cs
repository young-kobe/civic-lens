using System.Xml.Linq;

namespace CivicLens.Tests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly string Root = FindRoot();

    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["CivicLens.Core"] = [],
        ["CivicLens.Collection.Contracts"] = [],
        ["CivicLens.Publication.Contracts"] = [],
        ["CivicLens.Application"] = ["CivicLens.Core", "CivicLens.Collection.Contracts", "CivicLens.Publication.Contracts"],
        ["CivicLens.Infrastructure"] = ["CivicLens.Application"],
        ["CivicLens.Host"] = ["CivicLens.Application", "CivicLens.Infrastructure"],
        ["CivicLens.Collector"] = ["CivicLens.Collection.Contracts"]
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
            Assert.Equal(name == "CivicLens.Host" ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk",
                xml.Root!.Attribute("Sdk")!.Value);
            Assert.Empty(xml.Descendants("FrameworkReference")); // Host alone receives ASP.NET through the Web SDK.
            var references = xml.Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value));
            Assert.Equal(Allowed[name].Order(), references.Order());
            Assert.Empty(xml.Descendants("Reference")); // No binary references bypassing the project graph.

            string[] allowedPackages = name == "CivicLens.Infrastructure"
                ? ["AngleSharp", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Relational", "Npgsql.EntityFrameworkCore.PostgreSQL"]
                : name == "CivicLens.Collector" ? ["AngleSharp"]
                : name == "CivicLens.Host" ? ["Microsoft.AspNetCore.Authentication.OpenIdConnect"] : [];
            Assert.Equal(allowedPackages.Order(), xml.Descendants("PackageReference")
                .Select(element => element.Attribute("Include")!.Value).Order());
        }
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

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CivicLens.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate CivicLens.slnx.");
    }
}
