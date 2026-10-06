namespace CivicLens.Tests.Host;

public sealed class CollectorEnvironmentTests
{
    [UnixFact]
    public async Task CollectorChildDoesNotInheritDatabaseConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-environment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var probe = Path.Combine(directory, "probe.sh");
            var marker = Path.Combine(directory, "collector.dll");
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(marker, "not-started");
            await File.WriteAllTextAsync(probe, """
                #!/bin/sh
                if [ "${CIVIC_LENS_DATABASE+x}" = x ]; then
                    printf present > "$1"
                else
                    printf absent > "$1"
                fi
                exit 2
                """);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllTextAsync(config, """
                {"version":1,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","personIds":["person"],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);

            var result = await HostProcess.RunWithCollectorHostAsync(
                "Host=127.0.0.1;Database=fixture;Password=secret-sentinel", probe,
                "collect-import", config, "source", marker, Path.Combine(directory, "captures"));

            Assert.Equal("absent", await File.ReadAllTextAsync(marker));
            Assert.Equal(1, result.ExitCode); // The probe intentionally emits no receipt.
            Assert.Contains("Attempt ID:", result.Error);
            Assert.DoesNotContain("secret-sentinel", result.Output + result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

internal sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "The child-environment probe requires a POSIX shell.";
    }
}
