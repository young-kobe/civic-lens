using System.Reflection;
using System.Text.RegularExpressions;

namespace CivicLens.Application;

public sealed partial class CodeVersion
{
    public static readonly CodeVersion Unknown = new(null);

    private readonly string? sha;

    private CodeVersion(string? sha) => this.sha = sha;

    public string Sha => sha ?? throw new InvalidOperationException(
        "This build has no git commit SHA. Build from a git checkout, or pass -p:SourceRevisionId=<40 hex digits>.");

    public static CodeVersion Of(string sha)
    {
        if (!ShaPattern().IsMatch(sha)) throw new ArgumentException("A code version is 40 lowercase hex digits.", nameof(sha));
        return new(sha);
    }

    public static CodeVersion FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+') ?? -1;
        if (plus < 0) return Unknown;
        var candidate = informational![(plus + 1)..];
        return ShaPattern().IsMatch(candidate) ? new(candidate) : Unknown;
    }

    [GeneratedRegex(@"^[0-9a-f]{40}\z")]
    private static partial Regex ShaPattern();
}
