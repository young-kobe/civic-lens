using System.Reflection;
using System.Text.RegularExpressions;

namespace CivicLens.PublicApi.Pages;

/// <summary>The build that serves the pages. It is the git SHA that the build bakes into the assembly as <c>SourceRevisionId</c>. A page must change its ETag with each build, because a new build can change the HTML.</summary>
public static partial class BuildId
{
    public const string Unversioned = "unversioned";

    public static string FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+') ?? -1;
        return plus >= 0 && Token().IsMatch(informational![(plus + 1)..]) ? informational[(plus + 1)..] : Unversioned;
    }

    [GeneratedRegex(@"^[0-9A-Za-z]{7,64}\z")]
    private static partial Regex Token();
}
