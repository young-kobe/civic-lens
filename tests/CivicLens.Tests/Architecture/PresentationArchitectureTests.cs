using System.Text.RegularExpressions;

namespace CivicLens.Tests.Architecture;

public sealed class PresentationArchitectureTests
{
    private static readonly string HostRoot = Path.Combine(FindRoot(), "src", "CivicLens.Host");

    [Fact]
    public void ThemeColorsHaveOneOwnerAndPagesCannotIntroduceInlineStyles()
    {
        var assets = Path.Combine(HostRoot, "wwwroot");
        foreach (var file in Directory.GetFiles(assets, "*.css", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "theme.css") continue;
            Assert.False(Regex.IsMatch(File.ReadAllText(file), @"#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(", RegexOptions.IgnoreCase),
                $"{file} must use the shared theme's color tokens.");
            foreach (Match declaration in Regex.Matches(File.ReadAllText(file),
                @"(?:color|background(?:-color)?|fill|stroke|caret-color|border-color|outline-color)\s*:\s*([a-z-]+)\s*;", RegexOptions.IgnoreCase))
            {
                Assert.Contains(declaration.Groups[1].Value.ToLowerInvariant(),
                    new[] { "transparent", "currentcolor", "inherit", "initial", "unset", "revert", "canvastext" });
            }
        }

        foreach (var file in Directory.GetFiles(Path.Combine(HostRoot, "Pages"), "*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cshtml", StringComparison.Ordinal) || file.EndsWith(".razor", StringComparison.Ordinal)))
        {
            Assert.False(Regex.IsMatch(File.ReadAllText(file), @"<style\b|\bstyle\s*=", RegexOptions.IgnoreCase),
                $"{file} must use shared stylesheets.");
            if (file != Path.Combine(HostRoot, "Pages", "Shared", "_Layout.cshtml"))
            {
                Assert.False(Regex.IsMatch(File.ReadAllText(file), @"<html\b|<link\b", RegexOptions.IgnoreCase),
                    $"{file} must inherit the shared workspace shell and assets.");
            }
        }
    }

    [Fact]
    public void WorkspaceShellAndThemeAssetsHaveOneLayoutOwner()
    {
        var layouts = Directory.GetFiles(Path.Combine(HostRoot, "Pages"), "*Layout.cshtml", SearchOption.AllDirectories);
        var shell = Path.Combine(HostRoot, "Pages", "Shared", "_Layout.cshtml");
        Assert.Contains("css/theme.css", File.ReadAllText(shell), StringComparison.Ordinal);
        Assert.Contains("review-shell", File.ReadAllText(shell), StringComparison.Ordinal);
        foreach (var layout in layouts.Where(layout => layout != shell))
        {
            var text = File.ReadAllText(layout);
            Assert.DoesNotContain("<html", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<link", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/Pages/Shared/_Layout.cshtml", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EvidenceAndEditorialDiffsUseTheSharedRenderer()
    {
        foreach (var relativePath in new[] { "Documents/Comparison.cshtml", "Review/Editor.cshtml", "Review/Index.cshtml" })
        {
            var text = File.ReadAllText(Path.Combine(HostRoot, "Pages", relativePath));
            Assert.Contains("typeof(DocumentComparisonView)", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<DocumentComparisonView", text, StringComparison.Ordinal);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CivicLens.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate CivicLens.slnx.");
    }
}
