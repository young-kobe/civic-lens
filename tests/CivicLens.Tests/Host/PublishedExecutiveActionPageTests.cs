using System.Text.RegularExpressions;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Host;

public sealed class PublishedExecutiveActionPageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-exec-page-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ACorrectionIsPlainTextInTheFactsAndTheTimelineAndOnlyTheWordsOpenTheCorrectionLinkToIt()
    {
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a', withNotice: true);
        var directory = new FileReleaseDirectory(root);
        var publish = new PublishRelease(scenario.Publications, directory, new ReleaseRenderer(null, null), scenario.Loaders(),
            new PublicationCatalog(scenario.OfficialNames, IssueConfiguration.None), FixedRosters.NamedOnly(scenario.OfficialNames.Keys), PhotoCaptures.None(), scenario.Clock,
            new PruneReleases(scenario.Publications, directory, NullLogger<PruneReleases>.Instance));

        var release = await publish.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.ExecutiveAction, record)));

        var page = Regex.Replace(File.ReadAllText(Path.Combine(root, "releases", release.DirectoryName, PublicationProtocol.RecordPagePath(record))), @"\s+", " ");
        const string plain = "The Federal Register published a correction on 2024-11-20. This page shows the original document.";
        const string link = "https://www.federalregister.gov/documents/2024/11/20/R1-2024-26714/to-facilitate-positive-adjustment-to-competition-from-imports-of-fine-denier-polyester-staple-fiber";
        Assert.Equal(2, page.Split(plain).Length - 1);
        Assert.Contains($"{plain} <a href=\"{link}\">Open the correction</a> to see what changed.", page);
        Assert.Equal(1, page.Split($"<a href=\"{link}\"").Length - 1);
        Assert.Equal(1, page.Split("Open the correction").Length - 1);
    }
}
