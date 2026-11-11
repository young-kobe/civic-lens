using System.Text.RegularExpressions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Deploy;

public sealed partial class CaddyfileTests
{
    private static readonly string Caddyfile = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "deploy", "caddy", "Caddyfile"));

    [GeneratedRegex(@"^\thandle (?<path>\S+) \{\n\t\trewrite \* (?<target>\S+)\n\t\timport server_page\n\t\}$", RegexOptions.Multiline)]
    private static partial Regex ServerRoute();

    [Fact]
    public void EachServerPageRouteRewritesToItsWebRouteAndImportsTheSharedProxy()
    {
        var routes = ServerRoute().Matches(Caddyfile).ToDictionary(match => match.Groups["path"].Value, match => match.Groups["target"].Value);

        Assert.Equal(new Dictionary<string, string>
        {
            ["/compare.html"] = "/web/compare",
            ["/officials.html"] = "/web/officials",
            ["/boards.html"] = "/web/boards"
        }, routes);
    }

    [Fact]
    public void TheSharedProxyKeepsTheEncodingHeadersAndReachesTheApiPort()
    {
        var snippet = Caddyfile[Caddyfile.IndexOf("(server_page) {", StringComparison.Ordinal)..];
        snippet = snippet[..snippet.IndexOf("\n}\n", StringComparison.Ordinal)];

        Assert.Contains("encode zstd gzip", snippet);
        Assert.Contains("+Cache-Control no-transform", snippet);
        Assert.Contains("+Vary Accept-Encoding", snippet);
        Assert.Contains("reverse_proxy 127.0.0.1:{$CIVIC_LENS_API_PORT:8100}", snippet);
    }

    [Fact]
    public void EveryOtherPathOfTheSiteBlockStillGoesToTheStaticReleaseAndTheSearchPageIsNotMapped()
    {
        var site = Caddyfile[Caddyfile.IndexOf("http://civic-lens.info, http://127.0.0.1 {", StringComparison.Ordinal)..];
        site = site[..site.IndexOf("\nhttp://api.civic-lens.info", StringComparison.Ordinal)];
        var handles = Regex.Matches(site, @"^\thandle(?<path> \S+)? \{", RegexOptions.Multiline).Select(match => match.Groups["path"].Value.Trim());

        Assert.Equal(["/compare.html", "/officials.html", "/boards.html", ""], handles);
        Assert.Contains("file_server", site[site.LastIndexOf("\thandle {", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("/search.html", site);
    }
}
