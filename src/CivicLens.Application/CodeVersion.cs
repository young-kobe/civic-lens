using System.Reflection;
using System.Text.RegularExpressions;

namespace CivicLens.Application;

public sealed partial class CodeVersion
{
    public static readonly CodeVersion Unknown = new(null,
        "This build has no git commit SHA. Build from a git checkout, or pass -p:SourceRevisionId=<40 hex digits>.");

    private readonly string? sha;
    private readonly string problem;

    private CodeVersion(string? sha, string problem)
    {
        this.sha = sha;
        this.problem = problem;
    }

    public string Sha => sha ?? throw new InvalidOperationException(problem);

    public CodeVersion Require()
    {
        _ = Sha;
        return this;
    }

    public static CodeVersion FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+') ?? -1;
        if (plus < 0) return Unknown;
        var candidate = informational![(plus + 1)..];
        return ShaPattern().IsMatch(candidate)
            ? new(candidate, string.Empty)
            : new(null, $"The git commit SHA of this build is '{candidate}'. It must be 40 lowercase hex digits.");
    }

    [GeneratedRegex(@"^[0-9a-f]{40}\z")]
    private static partial Regex ShaPattern();
}
