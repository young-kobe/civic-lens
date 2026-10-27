using System.Text.RegularExpressions;

namespace CivicLens.Tests.Fixtures;

internal static class HostStyleSheets
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.Host", "wwwroot", "css", name));

    public static IEnumerable<string> Selectors(string css)
    {
        var withoutNoise = Regex.Replace(css, @"/\*.*?\*/|@keyframes\s+[\w-]+\s*\{(?:[^{}]|\{[^{}]*\})*\}", "", RegexOptions.Singleline);
        return Regex.Matches(withoutNoise, @"([^{};]+)\{")
            .Select(match => match.Groups[1].Value.Trim())
            .Where(prelude => !prelude.StartsWith('@'))
            .SelectMany(prelude => prelude.Split(','))
            .Select(selector => Regex.Replace(selector.Trim(), @"\s+", " "));
    }

    public static HashSet<string> ClassNames(string css) =>
        [.. Selectors(css).SelectMany(selector => Regex.Matches(selector, @"\.([A-Za-z_][\w-]*)").Select(match => match.Groups[1].Value))];
}
