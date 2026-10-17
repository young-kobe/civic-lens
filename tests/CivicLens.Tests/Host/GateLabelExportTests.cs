using System.Text.Json;
using CivicLens.Application.Statements;
using CivicLens.Host.Statements;

namespace CivicLens.Tests.Host;

public sealed class GateLabelExportTests : IDisposable
{
    private readonly string repo = Path.Combine(Path.GetTempPath(), "civic-gate-export-" + Guid.NewGuid().ToString("N"));
    private string Runtime => Path.Combine(repo, ".runtime");

    public GateLabelExportTests() => Directory.CreateDirectory(Runtime);

    public void Dispose() => Directory.Delete(repo, recursive: true);

    [Fact]
    public async Task EachLineHoldsOnlyTheFieldsTheBenchmarkNeedsSoNoSubjectNoteOrCredentialCanLeave()
    {
        var path = Path.Combine(Runtime, "labels", "gate.jsonl");
        var row = new GateLabelExportRow("c1", "e1", true, ["rent"], false, "Release text.");

        await GateLabelExport.WriteAsync([row, row with { CandidateId = "c2" }], path, Runtime, default);

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        var fields = JsonDocument.Parse(lines[0]).RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
        Assert.Equal(["candidateId", "extractionId", "heldOut", "issueIds", "namesBillOrVote", "text"], fields);
    }

    [Theory]
    [InlineData("../outside.jsonl")]
    [InlineData("../.runtime-evil/labels.jsonl")]
    public async Task APathOutsideTheRuntimeDirectoryIsRefusedAndNothingIsWritten(string path)
    {
        var target = Path.GetFullPath(path, Runtime);
        var row = new GateLabelExportRow("c1", "e1", false, [], false, "Text.");

        await Assert.ThrowsAsync<ArgumentException>(() => GateLabelExport.WriteAsync([row], Path.Combine(Runtime, path), Runtime, default));

        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task TheRuntimeDirectoryItselfIsNotAFilePathSoItIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => GateLabelExport.WriteAsync([], Runtime, Runtime, default));
    }
}
