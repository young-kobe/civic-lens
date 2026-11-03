using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ReadReleaseCapacityTests
{
    private const int Cap = PublicationProtocol.MaximumRecordsPerRelease;

    [Fact]
    public async Task TheHeldCountAddsWithdrawalsToRecordsBecauseTheyShareTheCap()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(Cap - 15, withFiles: false, [Withdrawn("a"), Withdrawn("b"), Withdrawn("c"), Withdrawn("d"), Withdrawn("e")]);
        scenario.Publications.ApprovedOutsideActiveRelease = 12;

        var capacity = await Read(scenario);

        Assert.Equal(new ReleaseCapacity(Cap - 10, 2), capacity);
    }

    [Fact]
    public async Task NoRecordWaitsForRoomWhileTheBacklogFitsTheFreeSlots()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(10, withFiles: false);
        scenario.Publications.ApprovedOutsideActiveRelease = 100;

        var capacity = await Read(scenario);

        Assert.Equal(new ReleaseCapacity(10, 0), capacity);
    }

    [Fact]
    public async Task AWithdrawnRecordWithANewApprovedRevisionDoesNotWaitBecauseItNeverPublishesAgain()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(Cap - 1, withFiles: false, [Withdrawn("a")]);
        scenario.Publications.ApprovedAgain.Add(new(PublishedRecordKind.DocumentChange, Id("a")));

        var capacity = await Read(scenario);

        Assert.Equal(new ReleaseCapacity(Cap, 0), capacity);
    }

    [Fact]
    public async Task TheManifestStaysUnreadWhenNoApprovedRecordWaitsBecauseTheHealthCheckRunsEveryTenMinutes()
    {
        var scenario = new PublicationScenario();
        scenario.SeedReleaseOfAnotherSchemaVersion();

        var capacity = await Read(scenario);

        Assert.Equal(new ReleaseCapacity(0, 0), capacity);
        Assert.Equal(1, scenario.Publications.CountCalls);
    }

    private static Task<ReleaseCapacity> Read(PublicationScenario scenario) =>
        new ReadReleaseCapacity(scenario.Publications, scenario.Releases).ExecuteAsync(default);

    private static string Id(string letter) => new(letter[0], 32);

    private static PublishedWithdrawal Withdrawn(string letter) => new()
    {
        Kind = PublishedRecordKind.DocumentChange,
        RecordId = Id(letter),
        WithdrawnRevisionNumber = 1,
        FirstPublishedAtUtc = PublicationScenario.ApprovedAt.AddDays(-1),
        WithdrawnAtUtc = PublicationScenario.ApprovedAt,
        Note = "Withdrawn.",
        Corrections = []
    };
}
