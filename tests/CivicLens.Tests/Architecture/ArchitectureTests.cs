using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
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
        ["CivicLens.Host"] = ["CivicLens.Application", "CivicLens.Infrastructure", "CivicLens.PublicWeb"],
        ["CivicLens.Collector"] = ["CivicLens.Collection.Contracts"],
        ["CivicLens.PublicApi"] = ["CivicLens.Publication.Contracts", "CivicLens.PublicWeb"],
        ["CivicLens.PublicWeb"] = ["CivicLens.Publication.Contracts"]
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
            Assert.Equal(name is "CivicLens.Host" or "CivicLens.PublicApi" ? "Microsoft.NET.Sdk.Web"
                : name == "CivicLens.PublicWeb" ? "Microsoft.NET.Sdk.Razor" : "Microsoft.NET.Sdk",
                xml.Root!.Attribute("Sdk")!.Value);
            // Host and PublicApi receive ASP.NET through the Web SDK. PublicWeb names the shared framework for Razor components.
            Assert.Equal(name == "CivicLens.PublicWeb" ? ["Microsoft.AspNetCore.App"] : [],
                xml.Descendants("FrameworkReference").Select(element => element.Attribute("Include")!.Value));
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
    public void ThePublicApiReferencesNoOperationalAssembly()
    {
        var references = ReferencedAssemblies(Path.Combine(AppContext.BaseDirectory, "CivicLens.PublicApi.dll"));

        Assert.Equal(["CivicLens.Publication.Contracts", "CivicLens.PublicWeb"], references.Where(name => name.StartsWith("CivicLens.", StringComparison.Ordinal)).Order());
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePublicWebReferencesOnlyThePublicationContracts()
    {
        var references = ReferencedAssemblies(Path.Combine(AppContext.BaseDirectory, "CivicLens.PublicWeb.dll"));

        Assert.Equal(["CivicLens.Publication.Contracts"], references.Where(name => name.StartsWith("CivicLens.", StringComparison.Ordinal)));
    }

    [Fact]
    public void ThePublicApiSqlNamesOnlyPublicApiViewsAndItsOwnSearchTables()
    {
        var apiRoot = Path.Combine(Root, "src", "CivicLens.PublicApi");
        var ownTables = Directory.GetFiles(Path.Combine(apiRoot, "Schema"))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"CREATE TABLE (?:IF NOT EXISTS )?(\w+)").Select(match => match.Groups[1].Value))
            .ToHashSet();
        Assert.NotEmpty(ownTables);

        var violations = new List<string>();
        foreach (var path in Directory.GetFiles(apiRoot, "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var source = File.ReadAllText(path);
            var readsViews = source.Contains("NpgsqlDataSource views", StringComparison.Ordinal)
                || path == Path.Combine(apiRoot, "Officials", "OfficialAttendanceReader.cs")
                || path == Path.Combine(apiRoot, "Officials", "OfficialActivityReader.cs");
            violations.AddRange(ForbiddenSqlTables(source, readsViews, ownTables)
                .Select(name => $"{Path.GetFileName(path)}: {name}"));
        }

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("SELECT 1 FROM public_api.votes v, publication_releases r WHERE true", "public_api.votes,publication_releases")]
    [InlineData("select 1 from publication_releases", "publication_releases")]
    [InlineData("SELECT 1 FROM public_api.votes v JOIN publication_releases r ON r.id = v.id", "public_api.votes,publication_releases")]
    [InlineData("SELECT 1 FROM public_api.votes AS v, public_api.vote_members AS m, record_state s", "public_api.votes,public_api.vote_members,record_state")]
    [InlineData("SELECT 1 FROM {table}", "{")]
    [InlineData("SELECT 1 FROM \"publication_releases\"", "\"")]
    [InlineData("SELECT 1 JOIN \"public_api\".\"votes\" v ON true", "\"")]
    [InlineData("SELECT 1 FROM \\\"publication_releases\\\"", "\\")]
    [InlineData("SELECT 1 FROM generate_series(1, 3)", "")]
    [InlineData("SELECT 1 FROM public_api.votes v JOIN LATERAL (SELECT 1 FROM public_api.vote_issues) i ON true", "public_api.votes,public_api.vote_issues")]
    public void TheSqlScanFindsEveryTableAFromOrJoinNamesWhateverTheCaseCommaListOrQuoting(string sql, string expected)
    {
        Assert.Equal(expected, string.Join(',', SqlTableNames(sql)));
    }

    [Fact]
    public void PublicViewQueriesAllowTheirOwnCteAliasesButCannotBorrowAliasesToReadPrivateTables()
    {
        const string source = """
            const string first = "WITH selected AS (SELECT 1 FROM public_api.bills) SELECT 1 FROM selected";
            const string second = "WITH actions AS (SELECT 1 FROM public_api.bills) SELECT 1 FROM actions";
            const string materialized = "WITH active AS MATERIALIZED (SELECT 1 FROM public_api.votes), picked AS NOT MATERIALIZED (SELECT 1 FROM active) SELECT 1 FROM picked";
            const string privateRead = "SELECT 1 FROM publication_releases";
            const string borrowedAlias = "SELECT 1 FROM actions";
            """;

        Assert.Equal(["publication_releases", "actions"], ForbiddenSqlTables(source, true, new HashSet<string>()));
    }

    private static IEnumerable<string> ForbiddenSqlTables(string source, bool readsViews, HashSet<string> ownTables)
    {
        var commonTables = CteNames(source);
        foreach (var statement in SqlLiterals(source).Where(IsStatement))
        {
            var statementCommonTables = CteNames(statement);
            foreach (var name in SqlTableNames(statement))
            {
                var allowed = readsViews
                    ? name.StartsWith("public_api.", StringComparison.Ordinal) || statementCommonTables.Contains(name)
                    : ownTables.Contains(name) || commonTables.Contains(name);
                if (!allowed) yield return name;
            }
        }
    }

    private static HashSet<string> CteNames(string sql) => Regex.Matches(sql, @"(?:WITH|,)\s*(\w+)\s+AS\s*(?:(?:NOT\s+)?MATERIALIZED\s*)?\(", RegexOptions.IgnoreCase)
        .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> SqlTableNames(string sql) =>
        Regex.Matches(sql, @"\b(?:FROM|JOIN)\s+(?<list>" + TableItem + @"(?:\s*,\s*" + TableItem + ")*)", RegexOptions.IgnoreCase)
            .SelectMany(clause => clause.Groups["name"].Captures.Select(capture => capture.Value.ToLowerInvariant()));

    private static IEnumerable<string> SqlLiterals(string source) =>
        Regex.Matches(source, @"\$?\""\""\""[\s\S]*?\""\""\""|\$?@?""(?:[^""\\]|\\.)*""").Select(match => match.Value);

    private static bool IsStatement(string literal) => Regex.IsMatch(literal, @"\b(?:SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase);

    private const string TableItem = @"(?<name>[{""\\]|(?!LATERAL\b)[\w.]+(?![\w.(]))(?:\s+(?:AS\s+)?\w+)?";

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
