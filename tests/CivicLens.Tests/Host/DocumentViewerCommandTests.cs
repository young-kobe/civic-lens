using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CivicLens.Tests.Host;

public sealed class DocumentViewerCommandTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("5080x")]
    public async Task ViewerRejectsInvalidPorts(string port)
    {
        var result = await HostProcess.RunAsync(null, "viewer", port);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("1 through 65535", result.Error);
    }

    [Fact]
    public async Task ViewerRequiresDatabaseWithoutEchoingConnectionValues()
    {
        var result = await HostProcess.RunAsync(null, "viewer", "5081");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("CIVIC_LENS_DATABASE", result.Error);
    }

    [Fact]
    public async Task ViewerBindsLoopbackAndAppliesRequestSafetyHeaders()
    {
        var port = GetAvailablePort();
        var assembly = GetHostAssemblyPath();
        var workingDirectory = Path.Combine(Path.GetTempPath(), "civic-viewer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("viewer");
        start.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment["CIVIC_LENS_DATABASE"] =
            "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";
        start.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:" + port;
        start.Environment["ASPNETCORE_Kestrel__Endpoints__Unsafe__Url"] = "http://0.0.0.0:" + port;

        using var process = Process.Start(start)!;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await WaitForViewerAsync(client, port, process);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.DoesNotContain("secret-sentinel", await response.Content.ReadAsStringAsync());

            using var rejectedHost = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");
            rejectedHost.Headers.Host = "example.com:" + port;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(rejectedHost)).StatusCode);

            using var rejectedMethod = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/");
            var methodResponse = await client.SendAsync(rejectedMethod);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, methodResponse.StatusCode);
            Assert.Contains("GET", methodResponse.Content.Headers.Allow);
            Assert.Contains("HEAD", methodResponse.Content.Headers.Allow);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> WaitForViewerAsync(HttpClient client, int port, Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException("Viewer exited before accepting requests.");
            try
            {
                return await client.GetAsync($"http://127.0.0.1:{port}/");
            }
            catch (HttpRequestException)
            {
                await Task.Delay(100);
            }
            catch (TaskCanceledException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException("Viewer did not become ready.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GetHostAssemblyPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CivicLens.slnx")))
            root = root.Parent;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.Combine(root!.FullName, "src", "CivicLens.Host", "bin", configuration, "net10.0", "CivicLens.Host.dll");
    }
}
