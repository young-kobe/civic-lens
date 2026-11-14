using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialFilesTests
{
    private static readonly string MayorVotes = PublicationProtocol.OfficialVotesPath("mayor", 119);
    private static readonly string ChairVotes = PublicationProtocol.OfficialVotesPath("chair", 119);

    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationKeepsAPageWithTheNameOfTheVoteRecordAndNoTerms()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["chair"] = ("C000127", null);
        scenario.RosterMembers = RosterFixtures.Members();
        await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a', memberIds: ["chair"]));
        scenario.OfficialNames.Remove("chair");

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        var files = scenario.Releases.Directories[second.DirectoryName];
        var chair = Assert.Single(Officials(files).Officials);
        Assert.Equal(("Council Chair Example", 0), (chair.Name, chair.Terms.Length));
        Assert.Contains(ChairVotes, files.Keys);
    }

    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationAndIsNamedByAnEntryTakesTheNameOfTheEntryWithoutReadingAVoteRecord()
    {
        var scenario = new PublicationScenario();
        await PublishAsync(scenario, "k1", PublishedRecordKind.Statement, scenario.AddStatement('a', speakerIds: ["chair"]));
        scenario.OfficialNames.Remove("chair");
        scenario.Releases.ReadPaths.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.Equal(["Council Chair Example"], Officials(scenario.Releases.Directories[second.DirectoryName]).Officials.Select(official => official.Name));
        Assert.DoesNotContain(scenario.Releases.ReadPaths, path => path.StartsWith("records/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APublishWithNoEarlierProfileStillNamesAnOfficialWhoLeftTheConfigurationFromTheRecords()
    {
        var scenario = new PublicationScenario();
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a', memberIds: ["chair"]));
        scenario.OfficialNames.Remove("chair");
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialsPath] =
            JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficials { Officials = [] }, PublicationProtocol.JsonOptions);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.Equal(["Council Chair Example"], Officials(scenario.Releases.Directories[second.DirectoryName]).Officials.Select(official => official.Name));
    }

    [Fact]
    public async Task APublishFailsLoudlyWhenNoRecordHoldsTheNameOfAnOfficialWhoLeftTheConfiguration()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a', memberIds: ["chair"]);
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, vote);
        scenario.OfficialNames.Remove("chair");
        var path = PublicationProtocol.RecordDataPath(vote);
        var record = JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[first.DirectoryName][path], PublicationProtocol.JsonOptions)!;
        scenario.Releases.Directories[first.DirectoryName][path] = JsonSerializer.SerializeToUtf8Bytes(
            record with { Members = [record.Members[0] with { Official = new() { Id = "other", Name = "Other" } }] }, PublicationProtocol.JsonOptions);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b'))));
    }

    [Fact]
    public async Task TheSummaryRowOfAnOfficialWithTwoCongressesHoldsThePreviousCongressSoABoardCanKeepShowingItAndAOneCongressRowHoldsNone()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');
        var chairOnly = scenario.AddVote('c', memberIds: ["chair"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer),
            new PublishedRecordReference(PublishedRecordKind.Vote, chairOnly)));

        var rows = Summaries(scenario, summary);
        var previous = rows["mayor"].Previous!;
        Assert.Equal((119, 118), (rows["mayor"].Congress, previous.Congress));
        Assert.Equal(1, Assert.Single(previous.Attendance).Held);
        var party = Assert.Single(previous.PartyVotes);
        Assert.Equal((0, 0), (party.SplitVotesCast, party.Breaks));
        Assert.Null(rows["chair"].Previous);
    }

    [Fact]
    public async Task ATouchedOfficialReusesTheCarriedPreviousFiguresWhenTheOlderCongressFileIsUnchangedAndComputesThemAgainWhenTheCarriedRowIsOutOfStep()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        var truth = Summaries(scenario, first)["mayor"].Previous!;
        var marked = truth with { Attendance = [.. truth.Attendance.Select(row => row with { Held = 99 })] };
        TamperPrevious(scenario, first, marked);

        var reused = await PublishAsync(scenario, "k2", PublishedRecordKind.Vote, scenario.AddVote('c'));
        Assert.Equal(99, Assert.Single(Summaries(scenario, reused)["mayor"].Previous!.Attendance).Held);

        TamperPrevious(scenario, reused, marked with { Congress = 117 });
        var recomputed = await PublishAsync(scenario, "k3", PublishedRecordKind.Vote, scenario.AddVote('d'));
        Assert.Equal(truth.Congress, Summaries(scenario, recomputed)["mayor"].Previous!.Congress);
        Assert.Equal(1, Assert.Single(Summaries(scenario, recomputed)["mayor"].Previous!.Attendance).Held);
    }

    [Fact]
    public async Task ANewVoteInTheOlderCongressChangesThePreviousFiguresAndAReplacementThatRemovesOneOfHerVotesLowersThemSoACarriedRowNeverGoesStale()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));

        var added = await PublishAsync(scenario, "k2", PublishedRecordKind.Vote, scenario.AddVote('c', congress: 118));
        Assert.Equal(2, Assert.Single(Summaries(scenario, added)["mayor"].Previous!.Attendance).Held);

        var review = scenario.Votes.Publishable[older].Review;
        var revised = review.CurrentRevision with { RevisionNumber = 2, Members = [review.CurrentRevision.Members[0] with { PersonId = "chair" }] };
        scenario.Votes.Publishable[older] = scenario.Votes.Publishable[older] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(older, 2, ReviewDecisionKind.Approve, 2) with { CorrectionNote = PublicationScenario.PersonNote }),
                ReviewStateVersion = 2
            }
        };
        var replaced = await ReplaceAsync(scenario, "k3", PublishedRecordKind.Vote, older);

        Assert.Equal(1, Assert.Single(Summaries(scenario, replaced)["mayor"].Previous!.Attendance).Held);
        Assert.Equal(118, Summaries(scenario, replaced)["chair"].Congress);
    }

    [Fact]
    public async Task ARewriteOfAllOfficialsComputesThePreviousFiguresAgainSoItCorrectsACarriedRow()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        var truth = Summaries(scenario, first)["mayor"].Previous!;
        TamperPrevious(scenario, first, truth with { Attendance = [.. truth.Attendance.Select(row => row with { Held = 99 })] });

        var rewritten = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteOfficials = true });

        Assert.Equal(1, Assert.Single(Summaries(scenario, rewritten)["mayor"].Previous!.Attendance).Held);
    }

    private static string Json(PublishedOfficialSummary row) => JsonSerializer.Serialize(row, PublicationProtocol.JsonOptions);

    private static Dictionary<string, PublishedOfficialSummary> Summaries(PublicationScenario scenario, PublicationReleaseSummary release) =>
        JsonSerializer.Deserialize<PublishedOfficialSummaries>(
            scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.OfficialSummariesPath], PublicationProtocol.JsonOptions)!.Officials.ToDictionary(row => row.Id);

    private static void TamperPrevious(PublicationScenario scenario, PublicationReleaseSummary release, PublishedVoteFigures previous)
    {
        var rows = Summaries(scenario, release);
        rows["mayor"] = rows["mayor"] with { Previous = previous };
        scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.OfficialSummariesPath] =
            JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficialSummaries { Officials = [.. rows.Values] }, PublicationProtocol.JsonOptions);
    }

    [Fact]
    public async Task AnOfficialsFileOverTheByteCapIsRefusedBeforeItIsWritten()
    {
        var scenario = new PublicationScenario();
        var title = new string('t', 256);
        var offices = Enumerable.Range(0, PublishedOfficials.MaximumTerms)
            .Select(index => new OfficeConfiguration { Title = title, StartsOn = new DateOnly(2000, 1, 1).AddDays(index) }).ToArray();
        var ids = Enumerable.Range(0, PublishedOfficials.MaximumOfficials).Select(index => $"p{index}").ToArray();
        foreach (var id in ids)
        {
            scenario.OfficialNames[id] = id;
            scenario.Offices[id] = offices;
        }
        await scenario.SeedReleaseWithEmptyCorrectionHistoriesAsync(withFiles: true, [.. ids.Chunk(PublishedRecordEntry.MaximumFilterValues).Select((chunk, index) => new PublishedRecordEntry
        {
            IssueIds = [],
            Kind = PublishedRecordKind.DocumentChange,
            PolicyAreaIds = [],
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = "Seeded",
            FirstPublishedAtUtc = PublicationScenario.ApprovedAt,
            Officials = [.. chunk.Select(id => new PublishedOfficial { Id = id, Name = id })]
        })]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('a'))));
    }

    [Fact]
    public async Task ARewriteOfAllOfficialsKeepsEveryRecordFirstPublicationDateAndEveryWithdrawal()
    {
        var scenario = new PublicationScenario();
        var wrong = scenario.AddStatement('e');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a')),
            new PublishedRecordReference(PublishedRecordKind.Statement, wrong)));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Statement, wrong)] = ReviewStatus.ApprovalWithdrawn;
        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Statement, wrong, "Wrong.")] });

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteOfficials = true });

        var before = Manifest(scenario.Releases.Directories[second.DirectoryName]);
        var after = Manifest(scenario.Releases.Directories[third.DirectoryName]);
        Assert.Equal(3, after.ReleaseNumber);
        Assert.Equal(before.Records.Select(record => (record.RecordId, record.FirstPublishedAtUtc)), after.Records.Select(record => (record.RecordId, record.FirstPublishedAtUtc)));
        Assert.Equal(wrong, Assert.Single(after.Withdrawals).RecordId);
        Assert.Contains(scenario.Releases.Links, link => link.Item2 == PublicationProtocol.RecordDataPath(before.Records[0].RecordId));
    }

    [Fact]
    public async Task TheRewriteOptionIsInThePayloadHashSoTheSameKeyReplaysWithItAndIsAMismatchWithoutIt()
    {
        var scenario = await TwoOfficialsWithPastCongressAsync();
        var request = new PublishReleaseRequest([], "k2") { RewriteOfficials = true };
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);
        var committed = scenario.Publications.Committed.Count;

        var replay = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);

        Assert.Equal(first, replay);
        Assert.Equal(committed, scenario.Publications.Committed.Count);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2")));
    }

    private static async Task<PublicationScenario> TwoOfficialsWithPastCongressAsync()
    {
        var scenario = new PublicationScenario();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b')),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c', speakerIds: ["chair"]))));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();
        return scenario;
    }

    private static PublicationRelease Manifest(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublicationRelease>(files[PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;

    [Fact]
    public async Task APublishThatNamesNoOfficialRewritesNoOfficialFile()
    {
        var scenario = new PublicationScenario();
        await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a'));
        scenario.Releases.WriteOrder.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.DoesNotContain(scenario.Releases.WriteOrder, path => path.StartsWith("officials/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AConfiguredOfficeIsTheTermOfAPersonWithNoRosterTerms()
    {
        var scenario = new PublicationScenario();
        scenario.Offices["mayor"] = [new() { Title = "Secretary of State", StartsOn = new(2025, 1, 21), Party = "Republican" }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var term = Assert.Single(Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms);
        Assert.Equal(("Secretary of State", "Republican", new DateOnly(2025, 1, 21), null), (term.Office, term.Party, term.Start, term.End));
    }

    [Theory]
    [InlineData("C000127", "Representative,Senator")]
    [InlineData("N000147", "Representative,Representative")]
    public async Task AMemberTermIsKindSenatorOrRepresentativeByItsChamberSoTheListGroupsASenatorWhoWasARepresentative(string bioguideId, string expected)
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (bioguideId, null);
        scenario.RosterMembers = RosterFixtures.Members();

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var kinds = Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Kind.ToString());
        Assert.Equal(expected.Split(','), kinds);
    }

    [Fact]
    public async Task AnExecutiveRosterTermIsKindExecutiveSoThePresidentIsNotInOtherOfficials()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("V000137", "Q22686");
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var terms = Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms;
        Assert.All(terms, term => Assert.Equal(PublishedOfficeKind.Executive, term.Kind));
    }

    [Theory]
    [InlineData(ConfiguredOfficeKind.Executive, PublishedOfficeKind.Executive)]
    [InlineData(ConfiguredOfficeKind.Judicial, PublishedOfficeKind.Judicial)]
    [InlineData(null, PublishedOfficeKind.Other)]
    public async Task AConfiguredOfficeTakesItsConfiguredKindElseOtherSoOnlyAnOfficeWithAKindLeavesTheOtherOfficialsTab(ConfiguredOfficeKind? kind, PublishedOfficeKind expected)
    {
        var scenario = new PublicationScenario();
        scenario.Offices["mayor"] = [new() { Title = "Chief Justice", StartsOn = new(2005, 9, 29), Kind = kind }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.Equal(expected, Assert.Single(Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms).Kind);
    }

    [Fact]
    public async Task APersonWithABioguideIdAndNoMemberTermShowsTheConfiguredOffice()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("M000001", null);
        scenario.Offices["mayor"] = [new() { Title = "Vice President", StartsOn = new(2025, 1, 20) }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.Equal(["Vice President"], Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Office));
    }

    [Fact]
    public async Task ExecutiveTermsWinOverConfiguredOfficesForAFormerMemberAbsentFromTheMemberSnapshot()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("V000137", "Q22686");
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();
        scenario.Offices["mayor"] = [new() { Title = "Secretary of State", StartsOn = new(2025, 1, 21) }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.Equal(["prez", "prez"], Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Office));
    }

    [Fact]
    public async Task RosterTermsWinOverConfiguredOfficesWhenBothExistForAPerson()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (null, "Q22686");
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();
        scenario.Offices["mayor"] = [new() { Title = "Secretary of State", StartsOn = new(2025, 1, 21) }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.DoesNotContain("Secretary of State", Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Office));
    }

    [Fact]
    public async Task APreviousOfficialsFileOverTheByteCapIsRefusedOnRead()
    {
        var scenario = new PublicationScenario();
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Statement, scenario.AddStatement('a'));
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialsPath] = new byte[PublishedOfficials.MaximumBytes + 1];

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b'))));
    }

    [Fact]
    public async Task AnOfficialWithNoVoteHasNoVoteFileAndAnEmptySummaryRow()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.DoesNotContain(files.Keys, path => path.StartsWith("officials/mayor/", StringComparison.Ordinal));
        Assert.Equal(Json(PublishedOfficialSummary.Empty("mayor") with { CosponsoredBills = 0 }), Json(Summaries(scenario, summary)["mayor"]));
    }

    [Fact]
    public async Task AnUntouchedOfficialKeepsItsSummaryRowByCarryAndItsVoteFilesByLink()
    {
        var scenario = new PublicationScenario();
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a'));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();
        scenario.Releases.ReadPaths.Clear();

        var second = await PublishAsync(scenario, "k2", PublishedRecordKind.Vote, scenario.AddVote('b', memberIds: ["chair"]));

        Assert.Equal(Json(Summaries(scenario, first)["mayor"]), Json(Summaries(scenario, second)["mayor"]));
        Assert.DoesNotContain(MayorVotes, scenario.Releases.ReadPaths);
        Assert.Contains((first.DirectoryName, MayorVotes), scenario.Releases.Links);
        Assert.DoesNotContain(MayorVotes, scenario.Releases.WriteOrder);
        Assert.Contains(ChairVotes, scenario.Releases.WriteOrder);
    }

    [Fact]
    public async Task AnOfficialVoteFileHoldsTheVotesOfThatOfficialOnly()
    {
        var scenario = new PublicationScenario();
        var mayorVote = scenario.AddVote('a');
        var chairVote = scenario.AddVote('b', memberIds: ["chair"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, mayorVote), new PublishedRecordReference(PublishedRecordKind.Vote, chairVote)));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal([mayorVote], VoteFile(files, MayorVotes).Votes.Select(vote => vote.RecordId));
        Assert.Equal([chairVote], VoteFile(files, ChairVotes).Votes.Select(vote => vote.RecordId));
    }

    [Fact]
    public async Task TheCurrentCongressIsTheNewestWithAVoteAndEarlierCongressesGetOneFileEach()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer)));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal([newer], VoteFile(files, MayorVotes).Votes.Select(vote => vote.RecordId));
        Assert.Equal([older], VoteFile(files, PublicationProtocol.OfficialVotesPath("mayor", 118)).Votes.Select(vote => vote.RecordId));
        Assert.Equal((119, 118), (Summaries(scenario, summary)["mayor"].Congress, Summaries(scenario, summary)["mayor"].Previous!.Congress));
    }

    [Fact]
    public async Task WhenTheNewestCongressEmptiesTheNextOlderCongressBecomesCurrentAndTheEmptiedFileIsDropped()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer)));
        MoveVoteToChair(scenario, newer);

        var second = await ReplaceAsync(scenario, "k2", PublishedRecordKind.Vote, newer);

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Equal(118, Summaries(scenario, second)["mayor"].Congress);
        Assert.Equal([older], VoteFile(files, PublicationProtocol.OfficialVotesPath("mayor", 118)).Votes.Select(vote => vote.RecordId));
        Assert.DoesNotContain(MayorVotes, files.Keys);
    }

    [Fact]
    public async Task ARewriteOfAllOfficialsComputesTheSummaryRowOfEveryOfficialAgainFromItsVoteFiles()
    {
        var scenario = await TwoOfficialsWithPastCongressAsync();
        var first = scenario.Publications.Committed.Single();
        var rows = Summaries(scenario, first);
        var truth = rows["mayor"];
        rows["mayor"] = truth with { Attendance = [.. truth.Attendance.Select(row => row with { Held = 99 })] };
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialSummariesPath] =
            JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficialSummaries { Officials = [.. rows.Values] }, PublicationProtocol.JsonOptions);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteOfficials = true });

        Assert.Equal(1, Assert.Single(Summaries(scenario, second)["mayor"].Attendance).Held);
        Assert.Contains((first.DirectoryName, MayorVotes), scenario.Releases.Links);
    }

    [Fact]
    public async Task ARewriteOfAllOfficialsKeepsTheRowsOfTheVoteFiles()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var kept = scenario.AddVote('b', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, kept),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('c'))));

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteOfficials = true });

        var rows = VoteFile(scenario.Releases.Directories[second.DirectoryName], PublicationProtocol.OfficialVotesPath("mayor", 118)).Votes;
        Assert.Equal([older, kept], rows.Select(vote => vote.RecordId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARewriteLeavesAWithdrawnVoteOffTheRewrittenVoteFiles()
    {
        var scenario = new PublicationScenario();
        var wrong = scenario.AddVote('a', congress: 118);
        var kept = scenario.AddVote('b', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, wrong),
            new PublishedRecordReference(PublishedRecordKind.Vote, kept),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('c'))));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Vote, wrong)] = ReviewStatus.ApprovalWithdrawn;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Vote, wrong, "Wrong.")] });

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteOfficials = true });

        var rows = VoteFile(scenario.Releases.Directories[third.DirectoryName], PublicationProtocol.OfficialVotesPath("mayor", 118)).Votes;
        Assert.Equal([kept], rows.Select(vote => vote.RecordId));
    }

    [Fact]
    public async Task AReplacedVoteTouchesTheOfficialsOfItsOldRevisionToo()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, vote), new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'))));
        MoveVoteToChair(scenario, vote);

        var second = await ReplaceAsync(scenario, "k2", PublishedRecordKind.Vote, vote);

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.DoesNotContain(MayorVotes, files.Keys);
        Assert.Equal([vote], VoteFile(files, ChairVotes).Votes.Select(row => row.RecordId));
        Assert.Equal(Json(PublishedOfficialSummary.Empty("mayor") with { CosponsoredBills = 0 }), Json(Summaries(scenario, second)["mayor"]));
    }

    [Fact]
    public async Task ARosterChangeLinksTheVoteFilesOfAnUntouchedOfficial()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("C000127", null);
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.DoesNotContain(scenario.Releases.WriteOrder, path => path.StartsWith("officials/mayor/", StringComparison.Ordinal));
        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 118)), scenario.Releases.Links);
        Assert.Contains((first.DirectoryName, MayorVotes), scenario.Releases.Links);
    }

    [Fact]
    public async Task AnUntouchedOfficialWhosePreviousReleaseHasNoSummaryRowGetsItsFilesWrittenAgain()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a'))));
        var rows = Summaries(scenario, first);
        rows.Remove("mayor");
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialSummariesPath] =
            JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficialSummaries { Officials = [.. rows.Values] }, PublicationProtocol.JsonOptions);
        scenario.Releases.WriteOrder.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        var row = Summaries(scenario, second)["mayor"];
        Assert.Equal((119, 1), (row.Congress, Assert.Single(row.Attendance).Held));
    }

    [Fact]
    public async Task ANewVoteOfTheCurrentCongressLeavesTheEarlierCongressFileLinkedAndANewerCongressLinksBoth()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var second = await PublishAsync(scenario, "k2", PublishedRecordKind.Vote, scenario.AddVote('c'));

        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 118)), scenario.Releases.Links);
        Assert.Contains(MayorVotes, scenario.Releases.WriteOrder);
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var third = await PublishAsync(scenario, "k3", PublishedRecordKind.Vote, scenario.AddVote('d', congress: 120));

        Assert.Contains((second.DirectoryName, MayorVotes), scenario.Releases.Links);
        Assert.Contains((second.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 118)), scenario.Releases.Links);
        var row = Summaries(scenario, third)["mayor"];
        Assert.Equal((120, 119), (row.Congress, row.Previous!.Congress));
    }

    [Fact]
    public async Task WithdrawingTheOnlyVoteOfAMiddleCongressDropsItsFileAndTheSummaryKeepsTheNextOlderCongressAsPrevious()
    {
        var scenario = new PublicationScenario();
        var middle = scenario.AddVote('b', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a')),
            new PublishedRecordReference(PublishedRecordKind.Vote, middle),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('c', congress: 117))));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Vote, middle)] = ReviewStatus.ApprovalWithdrawn;

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Vote, middle, "Wrong.")] });

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.DoesNotContain(PublicationProtocol.OfficialVotesPath("mayor", 118), files.Keys);
        Assert.Contains(PublicationProtocol.OfficialVotesPath("mayor", 117), files.Keys);
        Assert.Equal(117, Summaries(scenario, second)["mayor"].Previous!.Congress);
    }

    [Fact]
    public async Task TheAttendanceOfTheSummaryCountsTheMissedVotesOfTheVoteRows()
    {
        var scenario = new PublicationScenario();
        var missed = scenario.AddVote('a', cast: "Not Voting", totalLines: ["Party total D: Yea 0, Nay 0, Present 0, Not voting 1"]);
        var cast = scenario.AddVote('b');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, missed), new PublishedRecordReference(PublishedRecordKind.Vote, cast)));

        var chamber = Assert.Single(Summaries(scenario, summary)["mayor"].Attendance);
        Assert.Equal((PublishedChamber.House, 1, 2), (chamber.Chamber, chamber.Missed, chamber.Held));
    }

    private static void MoveVoteToChair(PublicationScenario scenario, string voteId)
    {
        var review = scenario.Votes.Publishable[voteId].Review;
        var revised = review.CurrentRevision with { RevisionNumber = 2, Members = [review.CurrentRevision.Members[0] with { PersonId = "chair" }] };
        scenario.Votes.Publishable[voteId] = scenario.Votes.Publishable[voteId] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(voteId, 2, ReviewDecisionKind.Approve, 2) with { CorrectionNote = PublicationScenario.PersonNote }),
                ReviewStateVersion = 2
            }
        };
    }

    private static Task<PublicationReleaseSummary> PublishAsync(PublicationScenario scenario, string key, PublishedRecordKind kind, string recordId) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request(key, new PublishedRecordReference(kind, recordId)));

    private static Task<PublicationReleaseSummary> ReplaceAsync(PublicationScenario scenario, string key, PublishedRecordKind kind, string recordId) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request(key, new PublishedRecordReference(kind, recordId)));

    private static PublishedOfficialVotes VoteFile(Dictionary<string, byte[]> files, string path) =>
        JsonSerializer.Deserialize<PublishedOfficialVotes>(files[path], PublicationProtocol.JsonOptions)!;

    private static PublishedOfficials Officials(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedOfficials>(files[PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
}
