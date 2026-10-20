using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialsTests
{
    private const string Cantwell = "C000127";
    private const string Trump = "Q22686";

    [Fact]
    public async Task OnlyAnOfficialWithAPublishedRecordIsListedSoNobodyAppearsBeforeTheirFirstRecord()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishAsync(scenario, scenario.AddStatement('a'));

        Assert.Equal(["mayor"], OfficialsOf(scenario, summary.DirectoryName).Officials.Select(official => official.Id));
    }

    [Fact]
    public async Task ACosponsorIsNotListedBecauseOnlyTheSponsorOwnsTheAction()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishAsync(scenario, scenario.AddAction('a'));

        Assert.Equal(["mayor"], OfficialsOf(scenario, summary.DirectoryName).Officials.Select(official => official.Id));
    }

    [Fact]
    public async Task AHouseVoteMemberIsListedEvenThoughTheVoteEntryNamesNoOfficial()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishAsync(scenario, scenario.AddVote('a'));

        Assert.Equal(["mayor"], OfficialsOf(scenario, summary.DirectoryName).Officials.Select(official => official.Id));
    }

    [Fact]
    public async Task AVoteMemberStaysListedWhenALaterReleaseCarriesTheVote()
    {
        var scenario = new PublicationScenario();
        await PublishAsync(scenario, scenario.AddVote('a'));

        scenario.Releases.ReadPaths.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.Equal(["mayor"], OfficialsOf(scenario, second.DirectoryName).Officials.Select(official => official.Id));
        Assert.DoesNotContain(scenario.Releases.ReadPaths, path => path.StartsWith("records/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReplacedVoteRevisionListsTheMembersOfTheNewRevisionNotTheCarriedOne()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a');
        await PublishAsync(scenario, vote);
        var review = scenario.Votes.Publishable[vote].Review;
        var revised = review.CurrentRevision with
        {
            RevisionNumber = 2,
            Members = [review.CurrentRevision.Members[0] with { PersonId = "chair" }]
        };
        scenario.Votes.Publishable[vote] = scenario.Votes.Publishable[vote] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(vote, 2, ReviewDecisionKind.Approve) with { RevisionNumber = 2 }),
                ReviewStateVersion = 2
            }
        };

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Vote, vote)));

        Assert.Equal(["chair"], OfficialsOf(scenario, second.DirectoryName).Officials.Select(official => official.Id));
    }

    [Fact]
    public async Task AReleaseWithNoOfficialRecordListsNoOfficial()
    {
        var scenario = new PublicationScenario();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.Empty(OfficialsOf(scenario, summary.DirectoryName).Officials);
    }

    [Fact]
    public async Task APersonWithoutRosterRowsIsListedByNameOnlyAndAWikidataIdWithoutASnapshotOfficialIsNotAnError()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("Z999999", "Q1");
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();

        var summary = await PublishAsync(scenario, scenario.AddStatement('a'));

        var mayor = Assert.Single(OfficialsOf(scenario, summary.DirectoryName).Officials);
        Assert.Equal(("Mayor Example", "Q1"), (mayor.Name, mayor.WikidataId));
        Assert.Empty(mayor.Terms);
    }

    [Fact]
    public async Task AMemberMatchedByBioguideGetsTheDatedTermsOfTheRoster()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (Cantwell, null);
        scenario.RosterMembers = RosterFixtures.Members();

        var summary = await PublishAsync(scenario, scenario.AddStatement('a'));

        var terms = Assert.Single(OfficialsOf(scenario, summary.DirectoryName).Officials).Terms;
        Assert.Equal([("rep", "WA", 1, 1993), ("sen", "WA", null, 2025)], terms.Select(term => (term.Office, term.State, term.District, term.Start.Year)));
        Assert.Equal(new DateOnly(2031, 1, 3), terms[1].End);
    }

    [Fact]
    public async Task AnExecutiveMatchedByWikidataGetsPresidentTermsWithNoStateOrDistrict()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (null, Trump);
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();

        var summary = await PublishAsync(scenario, scenario.AddStatement('a'));

        var terms = Assert.Single(OfficialsOf(scenario, summary.DirectoryName).Officials).Terms;
        Assert.Equal([("prez", "Republican", null, null), ("prez", "Republican", null, null)],
            terms.Select(term => (term.Office, term.Party, term.State, term.District)));
    }

    [Fact]
    public async Task AFormerMemberWhoIsNotInTheCurrentRosterFallsBackToTheExecutiveTermsByWikidata()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("B000444", "Q6279");
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();

        var summary = await PublishAsync(scenario, scenario.AddStatement('a'));

        var terms = Assert.Single(OfficialsOf(scenario, summary.DirectoryName).Officials).Terms;
        Assert.Equal(["viceprez", "viceprez", "prez"], terms.Select(term => term.Office));
    }

    [Fact]
    public async Task TheCatalogNamesEveryIssueAndPolicyAreaOfTheReleaseAndAnUnconfiguredIdNamesItself()
    {
        var scenario = new PublicationScenario
        {
            Issues = new IssueConfiguration
            {
                PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
                Issues = [new() { Id = "housing", Name = "Housing costs", Definition = "d", PolicyAreaIds = ["land-use"] }]
            }
        };
        scenario.SeedRelease(withFiles: true, [new PublishedRecordEntry
        {
            RecordId = 0.ToString("x32"),
            RevisionNumber = 1,
            Headline = "Seeded",
            FirstPublishedAtUtc = PublicationScenario.ApprovedAt.AddMinutes(-10),
            IssueIds = ["retired-issue"],
            PolicyAreaIds = ["retired-area"]
        }]);

        var summary = await PublishAsync(scenario, scenario.AddStatement('a'));

        var catalog = Read<PublishedCatalog>(scenario, summary.DirectoryName, PublicationProtocol.CatalogPath);
        Assert.Equal([("housing", "Housing costs"), ("retired-issue", "retired-issue")], catalog.Issues.Select(entry => (entry.Id, entry.Name)));
        Assert.Equal([("land-use", "Land use"), ("retired-area", "retired-area")], catalog.PolicyAreas.Select(entry => (entry.Id, entry.Name)));
    }

    private static Task<PublicationReleaseSummary> PublishAsync(PublicationScenario scenario, string recordId)
    {
        var kind = scenario.Statements.Publishable.ContainsKey(recordId) ? PublishedRecordKind.Statement
            : scenario.Actions.Publishable.ContainsKey(recordId) ? PublishedRecordKind.Action : PublishedRecordKind.Vote;
        return scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(kind, recordId)));
    }

    private static PublishedOfficials OfficialsOf(PublicationScenario scenario, string directory) =>
        Read<PublishedOfficials>(scenario, directory, PublicationProtocol.OfficialsPath);

    private static T Read<T>(PublicationScenario scenario, string directory, string path) =>
        JsonSerializer.Deserialize<T>(scenario.Releases.Directories[directory][path], PublicationProtocol.JsonOptions)!;
}
