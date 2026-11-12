using System.Diagnostics;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class OperatorCommandTests
{
    [Theory]
    [InlineData("1", null, 0)]
    [InlineData("9999", "2:vote:abc", 1)]
    [InlineData("1", "", 2)]
    public async Task TallyVerificationForwardsTheBoundedCountAndOptionalCursorAndPreservesTheExitCode(string count, string? cursor, int exitCode)
    {
        var arguments = cursor is null ? new[] { "releases", "tallies", "verify", count } : ["releases", "tallies", "verify", count, cursor];

        var result = await RunAsync(arguments, exitCode);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(new[] { "run", "--rm", "cli" }.Concat(arguments), result.ComposeArguments);
    }

    [Theory]
    [InlineData("releases", "tallies", "verify")]
    [InlineData("releases", "tallies", "verify", "0")]
    [InlineData("releases", "tallies", "verify", "01")]
    [InlineData("releases", "tallies", "verify", "10000")]
    [InlineData("releases", "tallies", "verify", "1", "1:a", "extra")]
    [InlineData("releases", "tallies", "verify 1", "2")]
    public async Task InvalidTallyArgumentsNeverInvokeCompose(params string[] arguments)
    {
        var result = await RunAsync(arguments, 0);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.ComposeArguments);
    }

    [Fact]
    public async Task CursorShellSyntaxIsForwardedLiterallyAndNeverExecuted()
    {
        var result = await RunAsync(["releases", "tallies", "verify", "1", "2:vote:abc; $(touch cursor-executed)"], 0);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2:vote:abc; $(touch cursor-executed)", result.ComposeArguments[^1]);
        Assert.False(result.CursorExecuted);
    }

    private static async Task<CommandResult> RunAsync(string[] arguments, int exitCode)
    {
        var folder = Directory.CreateTempSubdirectory("operator-command-").FullName;
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "operator-command.sh"));
            Assert.Contains("lock_file=/run/civic-lens-ops.lock", script, StringComparison.Ordinal);
            Assert.Contains("repository=/opt/civic-lens", script, StringComparison.Ordinal);
            script = script.Replace("lock_file=/run/civic-lens-ops.lock", "lock_file=" + Quote(Path.Combine(folder, "ops.lock")), StringComparison.Ordinal)
                .Replace("repository=/opt/civic-lens", "repository=" + Quote(folder), StringComparison.Ordinal);
            var wrapper = Path.Combine(folder, "operator-command.sh");
            await File.WriteAllTextAsync(wrapper, script);
            var compose = Path.Combine(folder, "deploy", "scripts", "compose.sh");
            Directory.CreateDirectory(Path.GetDirectoryName(compose)!);
            await File.WriteAllTextAsync(compose, """
                #!/usr/bin/env bash
                printf '%s\0' "$@" > "$CIVIC_LENS_TEST_ARGS"
                exit "$CIVIC_LENS_TEST_EXIT"
                """);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(compose, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var captured = Path.Combine(folder, "arguments");
            var start = new ProcessStartInfo("bash") { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(wrapper);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["CIVIC_LENS_TEST_ARGS"] = captured;
            start.Environment["CIVIC_LENS_TEST_EXIT"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            await Task.WhenAll(output, error);
            var forwarded = File.Exists(captured) ? (await File.ReadAllTextAsync(captured)).Split('\0')[..^1] : [];
            return new(process.ExitCode, forwarded, File.Exists(Path.Combine(folder, "cursor-executed")));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private sealed record CommandResult(int ExitCode, string[] ComposeArguments, bool CursorExecuted);
}
