using System.Diagnostics;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;

namespace CivicLens.Tests.Host;

public sealed class PublishedRelatedRecordsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-related-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ALinkMakesTheCitedPagesListEachOtherWhileTheirRecordFilesStayTheSameFilesOnDisk()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var action = scenario.AddAction('d');
        var publish = new PublishRelease(scenario.Publications, new FileReleaseDirectory(root), new ReleaseRenderer(), scenario.Loaders(),
            new PublicationCatalog(scenario.OfficialNames), scenario.Clock);
        var first = await publish.ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), new PublishedRecordReference(PublishedRecordKind.Action, action)));
        var relationship = scenario.AddRelationship('e', statement, action);

        var second = await publish.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Relationship, relationship)));

        foreach (var recordId in new[] { statement, action })
        {
            Assert.Equal(Inode(first, PublicationProtocol.RecordDataPath(recordId)), Inode(second, PublicationProtocol.RecordDataPath(recordId)));
            Assert.NotEqual(Inode(first, PublicationProtocol.RecordPagePath(recordId)), Inode(second, PublicationProtocol.RecordPagePath(recordId)));
        }
        Assert.Empty(RelatedLinks(first, statement));
        Assert.Equal([$"../records/{action}.html", $"../records/{relationship}.html"], RelatedLinks(second, statement));
        Assert.Equal([$"../records/{statement}.html", $"../records/{relationship}.html"], RelatedLinks(second, action));
    }

    private string[] RelatedLinks(PublicationReleaseSummary release, string recordId) =>
        [.. new HtmlParser().ParseDocument(File.ReadAllText(PathOf(release, PublicationProtocol.RecordPagePath(recordId))))
            .QuerySelectorAll("[aria-labelledby='related-title'] a").Select(link => link.GetAttribute("href")!)];

    private string PathOf(PublicationReleaseSummary release, string relativePath) =>
        Path.Combine(root, "releases", release.DirectoryName, relativePath);

    private string Inode(PublicationReleaseSummary release, string relativePath)
    {
        using var stat = Process.Start(new ProcessStartInfo("stat", ["-c", "%i", PathOf(release, relativePath)]) { RedirectStandardOutput = true })!;
        var inode = stat.StandardOutput.ReadToEnd().Trim();
        stat.WaitForExit();
        Assert.Equal(0, stat.ExitCode);
        return inode;
    }
}
