using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialPagesTests
{
    [Fact]
    public async Task AnOfficialWithNoPublishedRecordGetsNoPageSoNoEmptyProfileExists()
    {
        var scenario = new PublicationScenario();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a'))));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(PublicationProtocol.OfficialPagePath("mayor"), files.Keys);
        Assert.DoesNotContain(PublicationProtocol.OfficialPagePath("chair"), files.Keys);
        Assert.Contains("officials mayor</html>", Page(files, PublicationProtocol.OfficialsPagePath));
    }

    [Fact]
    public async Task AnOfficialPageHoldsTheRecordsAndVotesOfThatOfficialOnly()
    {
        var scenario = new PublicationScenario();
        var statementOfMayor = scenario.AddStatement('a');
        var statementOfChair = scenario.AddStatement('b', speakerIds: ["chair"]);
        var vote = scenario.AddVote('c', memberIds: ["chair"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statementOfMayor),
            new PublishedRecordReference(PublishedRecordKind.Statement, statementOfChair),
            new PublishedRecordReference(PublishedRecordKind.Vote, vote)));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal($"<html>official mayor {statementOfMayor} </html>", Page(files, PublicationProtocol.OfficialPagePath("mayor")));
        Assert.Equal($"<html>official chair {statementOfChair} {vote}</html>", Page(files, PublicationProtocol.OfficialPagePath("chair")));
    }

    [Fact]
    public async Task AnOfficialWhoseIdIsNotAPathSegmentGetsNoPageOrVoteFileBecauseNoSafeAddressExists()
    {
        var scenario = new PublicationScenario();
        scenario.OfficialNames["Bad/Id"] = "Bad Id";

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', memberIds: ["Bad/Id"]))));

        Assert.DoesNotContain(scenario.Releases.Directories[summary.DirectoryName].Keys, path => path.StartsWith("officials/", StringComparison.Ordinal));
    }

    private static string Page(Dictionary<string, byte[]> files, string path) => Encoding.UTF8.GetString(files[path]);
}
