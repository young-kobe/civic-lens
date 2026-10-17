using System.Text.Json;
using CivicLens.Application.Statements;

namespace CivicLens.Host.Statements;

public static class GateLabelExport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string DefaultRuntimeDirectory() => Path.GetFullPath(".runtime");

    public static string ResolveTarget(string path, string runtimeDirectory)
    {
        var target = Path.GetFullPath(path);
        return target.StartsWith(runtimeDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? target : throw new ArgumentException("The export path must be inside the .runtime directory.");
    }

    public static async Task WriteAsync(IEnumerable<GateLabelExportRow> rows, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllLinesAsync(target, rows.Select(row => JsonSerializer.Serialize(row, Json)), cancellationToken);
    }
}
