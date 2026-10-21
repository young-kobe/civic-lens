using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishWithdrawalsTests
{
    private static readonly ReviewActor Code = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    [Fact]
    public async Task AWithdrawalTakesTheRecordOutAndWritesANoticeWithTheRevisionTheDatesTheNoteAndTheHistory()
    {
        var (scenario, action) = await PublishedActionAsync();
        var firstPublishedAt = scenario.Clock.Now;
        scenario.ReviseAction(action, 2);
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Correct("k2", action, "Fix."));
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k3", Reference(action), "Wrong bill."));

        var files = scenario.Releases.Directories[third.DirectoryName];
        Assert.DoesNotContain(PublicationProtocol.RecordDataPath(action), files.Keys);
        Assert.Equal($"<html>withdrawn {action} Wrong bill.</html>", Page(files, PublicationProtocol.RecordPagePath(action)));
        var manifest = ReadManifest(scenario, third.DirectoryName);
        Assert.Empty(manifest.Records);
        var withdrawal = Assert.Single(manifest.Withdrawals);
        Assert.Equal((PublishedRecordKind.Action, action, 2, firstPublishedAt, scenario.Clock.Now, "Wrong bill."),
            (withdrawal.Kind, withdrawal.RecordId, withdrawal.WithdrawnRevisionNumber, withdrawal.FirstPublishedAtUtc, withdrawal.WithdrawnAtUtc, withdrawal.Note));
        var correction = Assert.Single(withdrawal.Corrections);
        Assert.Equal((1, "Fix."), (correction.ReplacedRevisionNumber, correction.Note));
        Assert.Equal(0, third.RecordCount);
    }

    [Fact]
    public async Task TheCommitNamesTheWithdrawnRecordSoTheStoreCanCheckThatReviewBlockedIt()
    {
        var (scenario, action) = await PublishedActionAsync();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), "Wrong bill."));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(Reference(action), Assert.Single(commit.WithdrawnRecords));
        Assert.Empty(commit.Records);
        Assert.Empty(commit.AddedRecords);
    }

    [Fact]
    public async Task ALaterPublishCarriesTheNoticeForwardAndNeverBringsTheWithdrawnFileBack()
    {
        var (scenario, action) = await PublishedActionAsync();
        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), "Wrong bill."));
        scenario.Releases.Links.Clear();
        var statement = scenario.AddStatement('b');

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k3", new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        Assert.Equal(action, Assert.Single(ReadManifest(scenario, third.DirectoryName).Withdrawals).RecordId);
        Assert.Contains((second.DirectoryName, PublicationProtocol.RecordPagePath(action)), scenario.Releases.Links);
        Assert.DoesNotContain(PublicationProtocol.RecordDataPath(action), scenario.Releases.Directories[third.DirectoryName].Keys);
    }

    [Fact]
    public async Task TheManifestListsTheNewestWithdrawalFirstAndBreaksATieByRecordId()
    {
        var scenario = new PublicationScenario();
        var records = new[] { scenario.AddAction('c'), scenario.AddAction('a'), scenario.AddAction('b'), scenario.AddAction('d') };
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", [.. records.Select(id => Reference(id))]));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(records[0]), "One."));
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3")
        {
            Withdrawals = [Note(records[3], "Two."), Note(records[1], "Two."), Note(records[2], "Two.")]
        });

        Assert.Equal([records[1], records[2], records[3], records[0]], ReadManifest(scenario, third.DirectoryName).Withdrawals.Select(item => item.RecordId));
    }

    [Fact]
    public async Task AWithdrawalNoteIsTrimmedAndKeepsOneCharacterForAWindowsLineBreakLikeACorrectionNote()
    {
        var (scenario, action) = await PublishedActionAsync();
        var note = "a\r\n" + new string('b', PublishedCorrection.MaximumNoteLength - 2);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), $"  {note}  "));

        Assert.Equal(note.Replace("\r\n", "\n"), Assert.Single(ReadManifest(scenario, second.DirectoryName).Withdrawals).Note);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0note")]
    public async Task ABlankOrNulWithdrawalNoteIsRefusedBecauseAReaderMustSeeWhy(string note)
    {
        var (scenario, action) = await PublishedActionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), note)));
    }

    [Fact]
    public async Task AWithdrawalNoteOverTheContractLengthIsRefusedAndTheLimitLengthIsAccepted()
    {
        var (scenario, action) = await PublishedActionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Withdraw("k2", Reference(action), new string('a', PublishedCorrection.MaximumNoteLength + 1))));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Withdraw("k3", Reference(action), new string('a', PublishedCorrection.MaximumNoteLength)));
    }

    [Fact]
    public async Task TheCodeSubjectCannotWithdrawBecauseOnlyAPersonJudgesThatARecordShouldGo()
    {
        var (scenario, action) = await PublishedActionAsync();
        var other = scenario.AddAction('d');

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(Code,
            new PublishReleaseRequest([Reference(other)], "k2") { Withdrawals = [Note(action, "Wrong.")] }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Publisher().ExecuteAsync(Code, Withdraw("k3", Reference(action), "Wrong.")));

        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ARecordOutsideTheActiveReleaseCannotBeWithdrawn()
    {
        var (scenario, _) = await PublishedActionAsync();
        var unpublished = scenario.AddAction('d');

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(unpublished), "Wrong.")));
    }

    [Fact]
    public async Task ARecordThatIsAlreadyWithdrawnCannotBeWithdrawnAgain()
    {
        var (scenario, action) = await PublishedActionAsync();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), "Wrong."));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k3", Reference(action), "Again.")));
    }

    [Fact]
    public async Task ARecordCannotBeBothPublishedAndWithdrawnInOneRequest()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([Reference(action)], "k2") { Withdrawals = [Note(action, "Wrong.")] }));
    }

    [Fact]
    public async Task TwoWithdrawalsOfOneRecordAndAnEmptyRequestAreRefused()
    {
        var (scenario, action) = await PublishedActionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [Note(action, "One."), Note(action, "Two.")] }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3")));
        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k4") { Withdrawals = default }));
    }

    [Fact]
    public async Task TheWithdrawalsAreInThePayloadHashSoTheSameKeyWithAnotherNoteIsAMismatchAndTheSameRequestReplays()
    {
        var (scenario, action) = await PublishedActionAsync();
        var request = Withdraw("k2", Reference(action), "Wrong.");
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);

        var replay = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);

        Assert.Equal(first, replay);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), "Other.")));
    }

    [Fact]
    public async Task APublishOfAWithdrawnRecordIsRefusedForAPersonAndForCode()
    {
        var (scenario, action) = await PublishedActionAsync();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), "Wrong."));
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<WithdrawnRecordException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k3", Reference(action))));
        await Assert.ThrowsAsync<WithdrawnRecordException>(() =>
            scenario.Publisher().ExecuteAsync(Code, PublicationScenario.Request("k4", Reference(action))));
    }

    [Fact]
    public async Task ARelationshipThatStaysInTheReleaseBlocksTheWithdrawalOfACitedRecordAndNamesTheCiter()
    {
        var (scenario, _, action, relationship) = await PublishedLinkAsync();
        var releases = scenario.Publications.Committed.Count;

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k3", Reference(action), "Wrong.")));

        Assert.Contains($"relationship:{relationship}", error.Message);
        Assert.Equal(releases, scenario.Publications.Committed.Count);
    }

    [Fact]
    public async Task WithdrawingTheCitingRelationshipInTheSameRequestPassesAndEachCitedPageDropsTheLink()
    {
        var (scenario, statement, action, relationship) = await PublishedLinkAsync();

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3")
        {
            Withdrawals = [Note(action, "Wrong."), Note(relationship, "Follows the action.", PublishedRecordKind.Relationship)]
        });

        var files = scenario.Releases.Directories[third.DirectoryName];
        Assert.Equal($"<html>statement {statement}</html>", Page(files, PublicationProtocol.RecordPagePath(statement)));
        Assert.Equal(2, ReadManifest(scenario, third.DirectoryName).Withdrawals.Length);
    }

    [Fact]
    public async Task AWithdrawnRelationshipRemovesItsLinkFromThePagesOfBothRecordsItCited()
    {
        var (scenario, statement, action, relationship) = await PublishedLinkAsync();
        Assert.Contains(" related e", Page(scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName], PublicationProtocol.RecordPagePath(action)));

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Withdraw("k3", Reference(relationship, PublishedRecordKind.Relationship), "Wrong link."));

        var files = scenario.Releases.Directories[third.DirectoryName];
        Assert.Equal($"<html>statement {statement}</html>", Page(files, PublicationProtocol.RecordPagePath(statement)));
        Assert.Equal($"<html>action {action}</html>", Page(files, PublicationProtocol.RecordPagePath(action)));
    }

    [Fact]
    public async Task AnotherLinkOfACitedRecordStaysOnItsPageWhenOneRelationshipIsWithdrawn()
    {
        var (scenario, statement, action, relationship) = await PublishedLinkAsync();
        var otherStatement = scenario.AddStatement('f');
        var other = scenario.AddRelationship('1', otherStatement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3",
            new PublishedRecordReference(PublishedRecordKind.Statement, otherStatement),
            new PublishedRecordReference(PublishedRecordKind.Relationship, other)));

        var fourth = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Withdraw("k4", Reference(relationship, PublishedRecordKind.Relationship), "Wrong link."));

        Assert.Equal($"<html>action {action} related 1</html>", Page(scenario.Releases.Directories[fourth.DirectoryName], PublicationProtocol.RecordPagePath(action)));
        Assert.Equal($"<html>statement {statement}</html>", Page(scenario.Releases.Directories[fourth.DirectoryName], PublicationProtocol.RecordPagePath(statement)));
    }

    [Fact]
    public async Task ANewRelationshipThatCitesARecordWithdrawnInTheSameRequestIsRefused()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var action = scenario.AddAction('d');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), Reference(action)));
        var link = scenario.AddRelationship('e', statement, action);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([Reference(link, PublishedRecordKind.Relationship)], "k2") { Withdrawals = [Note(action, "Wrong.")] }));

        Assert.Contains($"Relationship {link} cites a record", error.Message);
    }

    [Fact]
    public async Task ABriefThatStaysInTheReleaseBlocksTheWithdrawalOfACitedRecordUnlessTheBriefLeavesToo()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var brief = scenario.AddBrief(BriefFixtures.Week, [new(PublishedRecordKind.Statement, statement, 1)]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), new PublishedRecordReference(PublishedRecordKind.Brief, brief)));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(statement, PublishedRecordKind.Statement), "Wrong.")));
        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3")
        {
            Withdrawals = [Note(statement, "Wrong.", PublishedRecordKind.Statement), Note(brief, "Cites it.", PublishedRecordKind.Brief)]
        });

        Assert.Contains($"brief:{brief}", error.Message);
        Assert.Equal(2, ReadManifest(scenario, third.DirectoryName).Withdrawals.Length);
    }

    [Fact]
    public async Task AWithdrawnStatementLeavesTheOfficialPageAndAnOfficialWithNoRecordLeftLosesThePage()
    {
        var scenario = new PublicationScenario();
        var kept = scenario.AddStatement('a');
        var withdrawn = scenario.AddStatement('b');
        var chairOnly = scenario.AddStatement('c', speakerIds: ["chair"]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, kept), new PublishedRecordReference(PublishedRecordKind.Statement, withdrawn),
            new PublishedRecordReference(PublishedRecordKind.Statement, chairOnly)));

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [Note(withdrawn, "Wrong.", PublishedRecordKind.Statement), Note(chairOnly, "Wrong.", PublishedRecordKind.Statement)] });

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Equal($"<html>official mayor tracked current  past [] records [{kept}] votes []</html>", Page(files, PublicationProtocol.OfficialPagePath("mayor")));
        Assert.DoesNotContain(PublicationProtocol.OfficialPagePath("chair"), files.Keys);
    }

    [Fact]
    public async Task AWithdrawnVoteLeavesTheVoteFilesOfItsMembersAndACongressWithNoRowDisappears()
    {
        var scenario = new PublicationScenario();
        var recent = scenario.AddVote('a');
        var older = scenario.AddVote('b', congress: 118);
        var sameCongress = scenario.AddVote('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Votes(recent, older, sameCongress)));

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Withdraw("k2", Reference(older, PublishedRecordKind.Vote), "Wrong."));
        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Withdraw("k3", Reference(recent, PublishedRecordKind.Vote), "Wrong."));

        var afterOlder = scenario.Releases.Directories[second.DirectoryName];
        Assert.DoesNotContain(PublicationProtocol.OfficialVotesPath("mayor", 118), afterOlder.Keys);
        Assert.Equal(2, VoteRows(afterOlder, PublicationProtocol.OfficialVotesPath("mayor", 119)).Length);
        var afterRecent = scenario.Releases.Directories[third.DirectoryName];
        Assert.Equal([sameCongress], VoteRows(afterRecent, PublicationProtocol.OfficialVotesPath("mayor", 119)).Select(row => row.RecordId));
    }

    [Fact]
    public async Task AVotePageThatLinkedTheWithdrawnActionOfItsBillIsWrittenAgainWithoutTheLink()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('d');
        var vote = scenario.AddVote('a', billActionRecordIds: [action]);
        var unrelated = scenario.AddVote('b', billType: null);
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            Reference(action), Reference(vote, PublishedRecordKind.Vote), Reference(unrelated, PublishedRecordKind.Vote)));
        Assert.Equal($"<html>vote {vote} bill {action}</html>", Page(scenario.Releases.Directories[first.DirectoryName], PublicationProtocol.RecordPagePath(vote)));
        scenario.Releases.Links.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Withdraw("k2", Reference(action), "Wrong bill."));

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Equal($"<html>vote {vote} bill </html>", Page(files, PublicationProtocol.RecordPagePath(vote)));
        Assert.Contains((first.DirectoryName, PublicationProtocol.RecordPagePath(unrelated)), scenario.Releases.Links);
    }

    [Fact]
    public async Task ANullNoteOrNullWithdrawalIsRefusedWithAnArgumentExceptionBeforeAnythingIsRead()
    {
        var (scenario, action) = await PublishedActionAsync();
        var request = PublicationScenario.Request("k2", Reference(action));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request with { Withdrawals = [null!] }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request with { Notes = [null!] }));
    }

    private static async Task<(PublicationScenario Scenario, string Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Reference(action)));
        return (scenario, action);
    }

    private static async Task<(PublicationScenario Scenario, string Statement, string Action, string Relationship)> PublishedLinkAsync()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var action = scenario.AddAction('d');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), Reference(action)));
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Relationship, relationship)));
        return (scenario, statement, action, relationship);
    }

    private static PublishedRecordReference[] Votes(params string[] ids) =>
        [.. ids.Select(id => new PublishedRecordReference(PublishedRecordKind.Vote, id))];

    private static PublishedRecordReference Reference(string id, PublishedRecordKind kind = PublishedRecordKind.Action) => new(kind, id);

    private static RecordNote Note(string id, string text, PublishedRecordKind kind = PublishedRecordKind.Action) => new(kind, id, text);

    private static PublishReleaseRequest Withdraw(string key, PublishedRecordReference record, string note) =>
        new([], key) { Withdrawals = [new(record.Kind, record.RecordId, note)] };

    private static PublishReleaseRequest Correct(string key, string actionId, string note) =>
        new([Reference(actionId)], key) { Notes = [Note(actionId, note)] };

    private static string Page(Dictionary<string, byte[]> files, string path) => Encoding.UTF8.GetString(files[path]);

    private static PublishedOfficialVote[] VoteRows(Dictionary<string, byte[]> files, string path) =>
        JsonSerializer.Deserialize<PublishedOfficialVotes>(files[path], PublicationProtocol.JsonOptions)!.Votes;

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
