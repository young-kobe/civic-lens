using System.Text.RegularExpressions;

namespace CivicLens.Tests.Architecture;

public sealed class PresentationArchitectureTests
{
    private static readonly string HostRoot = Path.Combine(FindRoot(), "src", "CivicLens.Host");
    private static readonly string ComponentsRoot = Path.Combine(HostRoot, "Components");

    // Documents that own <html>: the workspace document and the published-page document.
    private static readonly string[] DocumentShells =
    [
        Path.Combine(ComponentsRoot, "App.razor"),
        Path.Combine(HostRoot, "Publication", "PublicPage.razor")
    ];

    [Fact]
    public void ThemeColorsHaveOneOwnerSoBothThemesStayComplete()
    {
        foreach (var file in StyleSheets().Where(file => Path.GetFileName(file) != "theme.css"))
        {
            var text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, @"#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(", RegexOptions.IgnoreCase),
                $"{file} must use the theme's color tokens.");
            foreach (Match declaration in Regex.Matches(text,
                @"(?:color|background(?:-color)?|fill|stroke|caret-color|border-color|outline-color)\s*:\s*([a-z-]+)\s*;", RegexOptions.IgnoreCase))
            {
                Assert.Contains(declaration.Groups[1].Value.ToLowerInvariant(),
                    new[] { "none", "transparent", "currentcolor", "inherit", "initial", "unset", "revert", "canvastext" });
            }
        }
    }

    [Fact]
    public void EveryDarkThemeTokenAlsoHasALightDefault()
    {
        var theme = File.ReadAllText(Path.Combine(HostRoot, "wwwroot", "css", "theme.css"));
        var lightBlock = theme[..theme.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.Ordinal)];
        var light = Tokens(lightBlock);
        foreach (var token in Tokens(theme[lightBlock.Length..]))
            Assert.Contains(token, light);
    }

    [Fact]
    public void MarkupCannotStyleItselfSoComponentsStayTheOnlyStyleOwners()
    {
        foreach (var file in Markup())
        {
            var text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, @"<style\b|\bstyle\s*=", RegexOptions.IgnoreCase), $"{file} must use the shared stylesheet.");
            if (DocumentShells.Contains(file)) continue;
            Assert.False(Regex.IsMatch(text, @"<html\b|<link\b|<script\b", RegexOptions.IgnoreCase),
                $"{file} must inherit its document shell and assets.");
        }
        Assert.Empty(Directory.GetFiles(HostRoot, "*.razor.css", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(HostRoot, "*.cshtml", SearchOption.AllDirectories));
    }

    [Fact]
    public void ShellsLoadTheSameThemeSoTheWorkspaceAndPublishedPagesMatch()
    {
        Assert.Contains("ThemeToggle", File.ReadAllText(DocumentShells[1]), StringComparison.Ordinal);
        Assert.Contains("ThemeToggle", File.ReadAllText(Path.Combine(ComponentsRoot, "Layout", "WorkspaceLayout.razor")), StringComparison.Ordinal);
        Assert.Contains("css/theme.css", File.ReadAllText(DocumentShells[0]), StringComparison.Ordinal);
        Assert.Contains("assets/css/theme.css", File.ReadAllText(Path.Combine(HostRoot, "Publication", "PublicAssets.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void PagesShowChangesThroughTheSharedComparisonComponent()
    {
        foreach (var file in Markup().Where(file => !file.StartsWith(ComponentsRoot, StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("<del", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<ins", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<string> StyleSheets() =>
        Directory.GetFiles(Path.Combine(HostRoot, "wwwroot"), "*.css", SearchOption.AllDirectories);

    private static IEnumerable<string> Markup() =>
        Directory.GetFiles(HostRoot, "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static HashSet<string> Tokens(string css) =>
        [.. Regex.Matches(css, @"(--[a-z0-9-]+)\s*:").Select(match => match.Groups[1].Value)];

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CivicLens.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate CivicLens.slnx.");
    }
}
