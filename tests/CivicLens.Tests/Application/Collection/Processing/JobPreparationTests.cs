using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Application.Paging;
using CivicLens.Application.Roster;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;
using EvidenceStatus = CivicLens.Application.Collection.Processing.EvidenceProcessingStatus;

namespace CivicLens.Tests.Application.Collection.Processing;

public sealed class JobPreparationTests
{
    private const string SourceUrl = "https://api.congress.gov/v3/bill";
    private const string DetailUrl = SourceUrl + "/119/hr/1?format=json";
    private const string CosponsorUrl = SourceUrl + "/119/hr/1/cosponsors?format=json&limit=250";
    private const string ActionsUrl = SourceUrl + "/119/hr/1/actions?format=json&limit=250";
    private const string SummariesUrl = SourceUrl + "/119/hr/1/summaries?format=json&limit=1";
    private const string VoteListUrl = HouseVoteFixtures.SourceUrl + "/119/1?format=json&limit=250";
    private const string VoteDetailUrl = HouseVoteFixtures.SourceUrl + "/119/1/100?format=json";
    private const string SenateMenuUrl = SenateVoteFixtures.SourceUrl + "/roll_call_lists/vote_menu_119_1.xml";
    private const string SenateVoteUrl = SenateVoteFixtures.SourceUrl + "/roll_call_votes/vote1191/vote_119_1_00001.xml";
    private const string VoteMembersUrl = HouseVoteFixtures.SourceUrl + "/119/1/100/members?format=json";
    private static readonly DateOnly Backfill = new(2025, 1, 3);
    private static readonly string ListUrl = new BillStreamWindow(Backfill, Backfill).RequestUrl(SourceUrl);
    private readonly RecordingBillAdmissions admissions = new();
    private readonly RecordingVoteAdmissions votes = new();
    private readonly RecordingRoster roster = new();
    private readonly RecordingExecutive executives = new();
    private readonly ThrowingProcessingStore processing = new();
    private readonly RecordingExtractions extractions = new();

    [Fact]
    public async Task DetailGateIsDecidedFromTheParsedBillAndStoredWithTheIssueRevision()
    {
        var revision = BillGateFixtures.Revision();

        var result = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.UntrackedPolicyArea, 0), revision);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var call = Assert.Single(admissions.Details);
        Assert.Equal(BillGateOutcome.Passed(BillGateReason.CoveredSponsor), call.Gate);
        Assert.Equal(revision.Id, call.IssueRevisionId);
        Assert.Equal(("job", 1), (call.JobId, call.Bill.Number));
        Assert.Equal((2, 1), (result.AdmittedCount, result.DuplicateCount));
    }

    [Theory]
    [InlineData("2025-01-02T19:00:00-05:00", BillGateDecision.Passed)]
    [InlineData("2025-01-03T04:00:00+05:00", BillGateDecision.NotRelevant)]
    public async Task CoverageIsReadOnTheUtcDayOfTheExactUpdateDate(string updateDate, BillGateDecision expected)
    {
        _ = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.UntrackedPolicyArea, 0, updateDate),
            BillGateFixtures.Revision());

        Assert.Equal(expected, Assert.Single(admissions.Details).Gate.Decision);
    }

    [Fact]
    public async Task ADetailJobWithoutAnIssuesConfigurationBlocksAndStoresNothing()
    {
        var result = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.TrackedPolicyArea, 0), null);

        Assert.Equal(EvidenceStatus.Blocked, result!.Status);
        Assert.Equal("issuesConfigurationUnavailable", result.ErrorCode);
        Assert.Empty(admissions.Details);
    }

    [Theory]
    [InlineData(ApiResource.Bill, DetailUrl)]
    [InlineData(ApiResource.BillCosponsors, CosponsorUrl)]
    [InlineData(ApiResource.BillActions, ActionsUrl)]
    [InlineData(ApiResource.BillSummaries, SummariesUrl)]
    public async Task AMalformedBillResponseIsInvalidDataSoTheWorkerBlocksThePermanentFailure(ApiResource resource, string url)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Prepare(resource, url, """{"unexpected":true}""", BillGateFixtures.Revision()));

        Assert.Empty(admissions.Details);
        Assert.Empty(admissions.CosponsorPages);
        Assert.Empty(admissions.ActionPages);
        Assert.Empty(admissions.SummaryPages);
    }

    [Fact]
    public async Task ACosponsorPageIsParsedAgainstItsBillAndPassedToTheStoreWithItsJob()
    {
        _ = await Prepare(ApiResource.BillCosponsors, CosponsorUrl, Cosponsors("A000001", next: null), BillGateFixtures.Revision());

        var call = Assert.Single(admissions.CosponsorPages);
        Assert.Equal("job", call.JobId);
        Assert.Equal("A000001", Assert.Single(call.Page.Cosponsors).BioguideId);
        Assert.Null(call.Page.Next);
    }

    [Fact]
    public async Task AnActionsPageIsParsedAndPassedToTheStoreWithItsNextLink()
    {
        var next = ActionsUrl + "&offset=250";

        _ = await Prepare(ApiResource.BillActions, ActionsUrl, $$$"""{"actions":[{"actionDate":"2025-01-03","text":"Introduced"}],"pagination":{"next":"{{{next}}}"}}""",
            BillGateFixtures.Revision());

        var call = Assert.Single(admissions.ActionPages);
        Assert.Equal(next, call.Page.Next);
    }

    [Fact]
    public async Task ASummariesPageIsParsedAndPassedToTheStoreWithItsNextLink()
    {
        var next = SummariesUrl + "&offset=250";

        _ = await Prepare(ApiResource.BillSummaries, SummariesUrl, Summaries(next), BillGateFixtures.Revision());

        var call = Assert.Single(admissions.SummaryPages);
        Assert.Equal(("job", next), (call.JobId, call.Page.Next));
        Assert.Equal("173", Assert.Single(call.Page.Summaries).VersionCode);
    }

    private static void AssertPinned(DocumentExtraction saved, string pinnedVersion, string constant, bool renderedTextMatches) =>
        Assert.True(saved.ExtractionVersion == pinnedVersion && renderedTextMatches,
            $"Extraction output changed. Bump {constant}, then update this test. Actual: {saved.ExtractionVersion}\n{saved.Text}");

    [Fact]
    public async Task ADetailPageIsExtractedOnceWithTheDetailParserAndTheCapturedAttempt()
    {
        _ = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.TrackedPolicyArea, 3), BillGateFixtures.Revision());

        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-bill-detail-v1", "CongressBillParser.ExtractionVersion",
            saved.Text == "Bill: 119 hr 1\nTitle: A bill\nIntroduced: 2025-01-03\nUpdated: 2025-01-03T12:00:00Z\n" +
            $"Policy area: {BillGateFixtures.TrackedPolicyArea}\nSponsor: A000001\nCosponsors (including withdrawn): 3\nActions: 1");
    }

    [Theory]
    [InlineData(ApiResource.Bill)]
    [InlineData(ApiResource.BillCosponsors)]
    [InlineData(ApiResource.BillActions)]
    [InlineData(ApiResource.BillSummaries)]
    public async Task EveryBillPageHandsItsSavedExtractionIdToAdmissionSoTheStoreCanCiteTheEvidence(ApiResource resource)
    {
        var (url, body) = resource switch
        {
            ApiResource.Bill => (DetailUrl, Detail("A000001", BillGateFixtures.TrackedPolicyArea, 3)),
            ApiResource.BillCosponsors => (CosponsorUrl, Cosponsors("A000001", next: null)),
            ApiResource.BillActions => (ActionsUrl, "{\"actions\":[],\"pagination\":{}}"),
            _ => (SummariesUrl, Summaries(null))
        };

        _ = await Prepare(resource, url, body, BillGateFixtures.Revision());

        Assert.Equal(Assert.Single(extractions.Saved).ExtractionId, Assert.Single(admissions.ExtractionIds));
    }

    [Fact]
    public async Task ACosponsorPageIsExtractedOnceWithTheCosponsorParser()
    {
        _ = await Prepare(ApiResource.BillCosponsors, CosponsorUrl, Cosponsors("A000001", next: null), BillGateFixtures.Revision());

        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-bill-cosponsors-v1", "CongressBillCosponsorsParser.ExtractionVersion",
            saved.Text == "Bill: 119 hr 1\nCosponsor: A000001, 2025-01-04");
    }

    [Fact]
    public async Task AnActionsPageIsExtractedOnceWithTheActionsParser()
    {
        _ = await Prepare(ApiResource.BillActions, ActionsUrl,
            """{"actions":[{"actionDate":"2025-01-03","actionTime":"09:30:00","text":"Introduced","type":"IntroReferral","actionCode":"H11100","sourceSystem":{"code":2,"name":"House floor actions"}}],"pagination":{}}""",
            BillGateFixtures.Revision());

        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-bill-actions-v1", "CongressBillActionsParser.ExtractionVersion",
            saved.Text == "Bill: 119 hr 1\nAction 2025-01-03 09:30:00 [type IntroReferral] [code H11100] [source 2 House floor actions]: Introduced");
    }

    [Fact]
    public async Task ASummariesPageIsExtractedOnceWithOneCombinedExtractionVersionAndTheSummaryConverted()
    {
        _ = await Prepare(ApiResource.BillSummaries, SummariesUrl, Summaries(null), BillGateFixtures.Revision());

        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("congress-bill-summaries-v1+fake-extraction", CongressBillSummariesParser.ExtractionVersion(new FakeTextExtractor()));
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-bill-summaries-v1+fake-extraction", "CongressBillSummariesParser.ExtractionVersion",
            saved.Text == "Bill: 119 hr 1\n\nSummary version: 173\nAction date: 2025-01-03\nAction: Introduced in House\n" +
            "Updated: 2025-01-04T10:00:00Z\nText:\ntext of <p>First</p>");
    }

    [Fact]
    public async Task ASummaryWhoseRenderedTextExceedsTheExtractionLimitFailsPreparationAndSavesNothing()
    {
        var fragment = new string('a', DocumentExtraction.MaximumTextLength);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Prepare(ApiResource.BillSummaries, SummariesUrl, Summaries(null), BillGateFixtures.Revision(), fragment));

        Assert.Empty(extractions.Saved);
        Assert.Empty(admissions.SummaryPages);
    }

    [Fact]
    public async Task ABillThatTheGateMarksNotRelevantIsStillExtracted()
    {
        _ = await Prepare(ApiResource.Bill, DetailUrl, Detail("Z000009", BillGateFixtures.UntrackedPolicyArea, 0), BillGateFixtures.Revision());

        Assert.Equal(BillGateDecision.NotRelevant, Assert.Single(admissions.Details).Gate.Decision);
        Assert.Single(extractions.Saved);
    }

    [Fact]
    public async Task TheExtractionIsWrittenEvenWhenAdmissionReportsAllJobsAsDuplicates()
    {
        admissions.DuplicatesOnly = true;

        var result = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.TrackedPolicyArea, 1), BillGateFixtures.Revision());

        Assert.Equal((0, 2), (result!.AdmittedCount, result.DuplicateCount));
        Assert.Single(extractions.Saved);
    }

    [Fact]
    public async Task PreparingTheSamePageAgainSavesTheSameExtractionIdentity()
    {
        var body = Cosponsors("A000001", next: null);

        _ = await Prepare(ApiResource.BillCosponsors, CosponsorUrl, body, BillGateFixtures.Revision());
        _ = await Prepare(ApiResource.BillCosponsors, CosponsorUrl, body, BillGateFixtures.Revision());

        Assert.Equal(2, extractions.Saved.Count);
        Assert.Single(extractions.Saved.Select(saved => saved.ExtractionId).Distinct());
    }

    [Fact]
    public async Task ABillListPageAndAnUnkindedJobAreNeverExtracted()
    {
        _ = await Prepare(ApiResource.BillList, ListUrl, """{"bills":[],"pagination":{}}""", BillGateFixtures.Revision());
        _ = await Prepare(null, SourceUrl, "{}", BillGateFixtures.Revision());

        Assert.Empty(extractions.Saved);
    }

    [Fact]
    public async Task APageThatFailsParsingIsNotExtracted()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Prepare(ApiResource.BillActions, ActionsUrl, """{"unexpected":true}""", BillGateFixtures.Revision()));

        Assert.Empty(extractions.Saved);
    }

    [Fact]
    public async Task ADetailJobWithoutAnIssuesConfigurationBlocksBeforeItWritesAnExtraction()
    {
        _ = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.TrackedPolicyArea, 0), null);

        Assert.Empty(extractions.Saved);
    }

    public static TheoryData<ApiResource?, string, string> EveryResource => new()
    {
        { ApiResource.BillList, ListUrl, """{"bills":[],"pagination":{}}""" },
        { ApiResource.Bill, DetailUrl, Detail("Z000009", BillGateFixtures.TrackedPolicyArea, 0) },
        { ApiResource.BillCosponsors, CosponsorUrl, Cosponsors("Z000009", next: null) },
        { ApiResource.BillActions, ActionsUrl, """{"actions":[],"pagination":{}}""" },
        { ApiResource.BillSummaries, SummariesUrl, """{"summaries":[],"pagination":{}}""" },
        { ApiResource.HouseVoteList, VoteListUrl, """{"houseRollCallVotes":[],"pagination":{}}""" },
        { ApiResource.HouseVoteDetail, VoteDetailUrl, HouseVoteFixtures.Detail() },
        { ApiResource.HouseVoteMembers, VoteMembersUrl, HouseVoteFixtures.Members() },
        { ApiResource.SenateVoteMenu, SenateMenuUrl, Encoding.UTF8.GetString(SenateVoteFixtures.Menu()) },
        { ApiResource.SenateVote, SenateVoteUrl, Encoding.UTF8.GetString(SenateVoteFixtures.Vote()) },
        { ApiResource.MemberRoster, RosterFixtures.SourceUrl, RosterFixtures.Json },
        { ApiResource.ExecutiveRoster, ExecutiveFixtures.SourceUrl, ExecutiveFixtures.Json },
        { ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl(), "the photo fixture replaces this text" }
    };

    [Fact]
    public async Task ASenateMenuIsParsedAgainstTheSessionOfItsUrlAndPassedToTheStoreWithItsJob()
    {
        var result = await Prepare(ApiResource.SenateVoteMenu, SenateMenuUrl, Encoding.UTF8.GetString(SenateVoteFixtures.Menu()), null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var call = Assert.Single(votes.SenateMenus);
        Assert.Equal(("job", 2), (call.JobId, call.Menu.Votes.Count));
        Assert.Empty(extractions.Saved);
    }

    [Fact]
    public async Task ASenateVoteIsExtractedOnceAndTheStoreGetsThatExtractionSoTheCandidateBindsTheExactEvidence()
    {
        var result = await Prepare(ApiResource.SenateVote, SenateVoteUrl, Encoding.UTF8.GetString(SenateVoteFixtures.Vote()), null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-senate-vote-v1", "SenateVoteParser.ExtractionVersion",
            saved.Text.Contains("\nMember: S428, Angela Alsobrooks, D, MD, Yea\n", StringComparison.Ordinal));
        var call = Assert.Single(votes.SenateVotes);
        Assert.Equal(("job", SenateVoteFixtures.Roll1, saved.ExtractionId), (call.JobId, call.Vote.Roll, call.ExtractionId));
    }

    [Fact]
    public async Task ASenateVoteFileForAnotherRollThanItsUrlIsRefused()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Prepare(ApiResource.SenateVote, SenateVoteUrl, Encoding.UTF8.GetString(SenateVoteFixtures.Vote(voteNumber: "2")), null));
    }

    [Fact]
    public async Task AVoteListPageIsParsedAgainstItsSessionAndPassedToTheStoreWithItsJob()
    {
        var result = await Prepare(ApiResource.HouseVoteList, VoteListUrl, """
            {"houseRollCallVotes":[{"congress":119,"sessionNumber":1,"rollCallNumber":100,"updateDate":"2025-06-24T08:34:41-04:00"}],
             "pagination":{}}
            """, null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var call = Assert.Single(votes.ListPages);
        Assert.Equal("job", call.JobId);
        Assert.Equal(HouseVoteFixtures.Roll100, Assert.Single(call.Page.Votes).Roll);
    }

    [Fact]
    public async Task AVoteDetailIsParsedAgainstTheRollOfItsUrlAndNeedsNoIssueConfiguration()
    {
        var result = await Prepare(ApiResource.HouseVoteDetail, VoteDetailUrl, HouseVoteFixtures.Detail(), null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Equal(("job", HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated)), Assert.Single(votes.Details));
        Assert.Equal((1, 0), (result.AdmittedCount, result.DuplicateCount));
    }

    [Fact]
    public async Task AVoteDetailIsExtractedOnceAndTheStoreGetsThatExtractionSoTheCandidateBindsTheExactEvidence()
    {
        _ = await Prepare(ApiResource.HouseVoteDetail, VoteDetailUrl, HouseVoteFixtures.Detail(), null);

        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-house-vote-v1", "HouseVoteParser.ExtractionVersion",
            saved.Text.StartsWith("House vote: 119 1 100\nUpdated: 2025-06-24T12:34:41Z\n", StringComparison.Ordinal));
        Assert.Equal(saved.ExtractionId, Assert.Single(votes.ExtractionIds));
    }

    [Fact]
    public async Task VoteMembersAreExtractedOnceAndTheStoreGetsThatExtraction()
    {
        _ = await Prepare(ApiResource.HouseVoteMembers, VoteMembersUrl, HouseVoteFixtures.Members(), null);

        var saved = Assert.Single(extractions.Saved);
        Assert.Equal("attempt", saved.SourceAttempt.AttemptId);
        AssertPinned(saved, "congress-house-vote-members-v1", "HouseVoteMembersParser.ExtractionVersion",
            saved.Text.Contains("\nMember: A000148, Jake Auchincloss, D, MA, Nay\n", StringComparison.Ordinal));
        Assert.Equal(saved.ExtractionId, Assert.Single(votes.ExtractionIds));
    }

    [Fact]
    public async Task VoteMembersCarryTheirOwnUpdateDateToTheStoreSoAVersionMismatchWithTheDetailCanBeRefused()
    {
        _ = await Prepare(ApiResource.HouseVoteMembers, VoteMembersUrl, HouseVoteFixtures.Members("2025-06-25T08:34:41-04:00"), null);

        var call = Assert.Single(votes.Members);
        Assert.NotEqual(HouseVoteFixtures.Updated, call.Vote.UpdateDate);
        Assert.Equal(HouseVoteFixtures.Updated.AddDays(1), call.Vote.UpdateDate);
    }

    [Fact]
    public async Task VoteMembersAreParsedAgainstTheRollOfTheirUrlAndPassedToTheStoreWithTheirJob()
    {
        var result = await Prepare(ApiResource.HouseVoteMembers, VoteMembersUrl, HouseVoteFixtures.Members(), null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Equal(("job", HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated)), Assert.Single(votes.Members));
    }

    [Theory]
    [InlineData(ApiResource.HouseVoteList, VoteListUrl)]
    [InlineData(ApiResource.HouseVoteDetail, VoteDetailUrl)]
    [InlineData(ApiResource.HouseVoteMembers, VoteMembersUrl)]
    public async Task AMalformedVoteResponseIsInvalidDataSoTheWorkerBlocksThePermanentFailure(ApiResource resource, string url)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Prepare(resource, url, """{"unexpected":true}""", null));

        Assert.Empty(votes.ListPages);
        Assert.Empty(votes.Details);
        Assert.Empty(votes.Members);
    }

    [Fact]
    public async Task ARollCallOtherThanTheOneInTheJobUrlIsInvalidDataSoNoVersionIsRecordedForTheWrongVote()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Prepare(ApiResource.HouseVoteDetail, VoteDetailUrl, HouseVoteFixtures.Detail(roll: 101), null));

        Assert.Empty(votes.Details);
    }

    [Fact]
    public async Task ARosterJobIsParsedAndPassedToTheStoreWithTheAttemptOfItsCapture()
    {
        var result = await Prepare(ApiResource.MemberRoster, RosterFixtures.SourceUrl, RosterFixtures.Json, null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var call = Assert.Single(roster.Admissions);
        Assert.Equal(("attempt", 3), (call.AttemptId, call.Members.Count));
    }

    [Fact]
    public async Task ANewRosterBodyPassesTheEnabledPhotoSourceSoTheSnapshotAndItsPhotoJobsShareOneTransaction()
    {
        var configuration = MemberPhotoFixtures.Configuration();

        _ = await Prepare(ApiResource.MemberRoster, RosterFixtures.SourceUrl, RosterFixtures.Json, null, rosterConfiguration: configuration);

        Assert.Equal(MemberPhotoFixtures.SourceId, Assert.Single(roster.PhotoSourcesOfAdmissions)!.SourceId);
        Assert.Empty(roster.PhotoJobAdmissions);
    }

    [Fact]
    public async Task AKnownRosterBodyStillEnqueuesPhotoJobsSoEachDailyCheckRefreshesTheMonthlyPhotoWindow()
    {
        roster.Known = true;

        _ = await Prepare(ApiResource.MemberRoster, RosterFixtures.SourceUrl, "not json at all", null,
            rosterConfiguration: MemberPhotoFixtures.Configuration());

        Assert.Equal(MemberPhotoFixtures.SourceId, Assert.Single(roster.PhotoJobAdmissions).SourceId);
        Assert.Empty(roster.Admissions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WithoutAnEnabledPhotoSourceTheRosterJobEnqueuesNoPhotoJobs(bool known, bool sourceDisabled)
    {
        roster.Known = known;
        var configuration = sourceDisabled
            ? MemberPhotoFixtures.Configuration() with { Sources = [RosterFixtures.Source(), MemberPhotoFixtures.Source() with { Enabled = false }] }
            : RosterFixtures.Configuration();

        _ = await Prepare(ApiResource.MemberRoster, RosterFixtures.SourceUrl, known ? "not json at all" : RosterFixtures.Json, null,
            rosterConfiguration: configuration);

        Assert.Empty(roster.PhotoJobAdmissions);
        Assert.All(roster.PhotoSourcesOfAdmissions, photos => Assert.Null(photos));
    }

    [Fact]
    public async Task AKnownRosterCaptureIsNotParsedAndWritesNothing()
    {
        roster.Known = true;

        var result = await Prepare(ApiResource.MemberRoster, RosterFixtures.SourceUrl, "not json at all", null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Empty(roster.Admissions);
    }

    [Fact]
    public async Task AnExecutiveJobIsParsedAndPassedToItsOwnStoreAndNotToTheMemberStore()
    {
        var result = await Prepare(ApiResource.ExecutiveRoster, ExecutiveFixtures.SourceUrl, ExecutiveFixtures.Json, null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var call = Assert.Single(executives.Admissions);
        Assert.Equal(("attempt", 3), (call.AttemptId, call.Officials.Count));
        Assert.Empty(roster.Admissions);
    }

    [Fact]
    public async Task AKnownExecutiveCaptureIsNotParsedAndWritesNothing()
    {
        executives.Known = true;

        var result = await Prepare(ApiResource.ExecutiveRoster, ExecutiveFixtures.SourceUrl, "not json at all", null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Empty(executives.Admissions);
    }

    [Fact]
    public async Task AMemberPhotoJobPassesThePersonSourceUrlAndCreditWithItsAttemptToTheStore()
    {
        var result = await Prepare(ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl(), "", null);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Equal(new MemberPhoto("attempt", "a-person", MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit),
            Assert.Single(roster.Photos));
    }

    [Theory]
    [InlineData("image/png", 225, 275, false)]
    [InlineData("text/html", 225, 275, false)]
    [InlineData("image/jpeg", 200, 275, false)]
    [InlineData("image/jpeg", 225, 300, false)]
    [InlineData("image/jpeg", 225, 275, true)]
    public async Task APhotoThatIsNotAJpegOfThePublishedSizeEndsFailedAsInvalidDataAndWritesNoRow(string contentType,
        int width, int height, bool breakSignature)
    {
        var bytes = MemberPhotoFixtures.Jpeg(width, height);
        if (breakSignature) bytes[2] = 0x00;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Prepare(ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl(), "", null, bytes: bytes, contentType: contentType));

        Assert.Empty(roster.Photos);
    }

    [Fact]
    public async Task APhotoJobOfAPersonNoLongerInTheConfigurationIsInvalidDataSoNoRowNamesAStranger()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Prepare(ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl("B000002"), "", null));

        Assert.Empty(roster.Photos);
    }

    [Fact]
    public void EveryApiResourceHasACaseInTheNeverExtractedTheory()
    {
        var covered = EveryResource.Select(row => (ApiResource?)row[0]).OfType<ApiResource>();

        Assert.Equal(Enum.GetValues<ApiResource>().Order(), covered.Order());
    }

    [Fact]
    public async Task AnApiJobWithoutAResourceIsBlockedInsteadOfSilentlyPrepared()
    {
        var result = await Prepare(null, SourceUrl, "{}", BillGateFixtures.Revision());

        Assert.Equal(EvidenceStatus.Blocked, result!.Status);
        Assert.Equal("apiResourceMissing", result.ErrorCode);
        Assert.Equal(0, processing.EnsureCalls);
    }

    [Theory]
    [MemberData(nameof(EveryResource))]
    public async Task ApiJobsNeverGetAnExtractionStage(ApiResource? resource, string url, string body)
    {
        var result = await Prepare(resource, url, body, BillGateFixtures.Revision());

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Equal(0, processing.EnsureCalls);
    }

    private async Task<JobPreparationResult?> Prepare(ApiResource? resource, string url, string body,
        IssueConfigurationRevision? issues, string? fragmentText = null, byte[]? bytes = null, string? contentType = null,
        CollectionConfiguration? rosterConfiguration = null)
    {
        var isPhoto = resource == ApiResource.MemberPhoto;
        bytes ??= isPhoto ? MemberPhotoFixtures.Jpeg() : Encoding.UTF8.GetBytes(body);
        contentType ??= isPhoto ? "image/jpeg" : "application/json";
        var textExtractor = new FakeTextExtractor(fragmentText);
        var configured = resource switch
        {
            ApiResource.MemberRoster => new ConfiguredCollectionSource(rosterConfiguration ?? RosterFixtures.Configuration(), RosterFixtures.SourceId, Backfill),
            ApiResource.ExecutiveRoster => new ConfiguredCollectionSource(ExecutiveFixtures.Configuration(), ExecutiveFixtures.SourceId, Backfill),
            ApiResource.MemberPhoto => new ConfiguredCollectionSource(
                MemberPhotoFixtures.Configuration(RosterFixtures.Person("a-person", "A Person", MemberPhotoFixtures.Bioguide)),
                MemberPhotoFixtures.SourceId, Backfill),
            ApiResource.SenateVoteMenu or ApiResource.SenateVote =>
                new ConfiguredCollectionSource(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, Backfill),
            _ when IsHouseVote(resource) => new ConfiguredCollectionSource(HouseVoteFixtures.Configuration(), HouseVoteFixtures.SourceId, Backfill),
            _ => new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", Backfill)
        };
        var definition = configured.CreateApiJobDefinition(resource ?? ApiResource.BillList, resource is null ? ListUrl : url, Backfill, CollectionJobPriority.Check);
        if (resource is null) definition = definition with { ApiResource = null };
        var attempt = new CollectionJobAttempt("attempt", definition.CreateRequest("job", Path.GetFullPath("captures")),
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var job = new CollectionJobRecord("job", definition, "key", CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null,
            false, [attempt]);
        var preparation = new JobPreparation(new SingleJob(job), new CapturedAttempts(contentType), new NoDiscoveryAdmissions(), admissions, votes, roster, executives,
            new FixedBody(bytes), processing, extractions, textExtractor, issues);
        return await preparation.PrepareAsync("job", "artifacts", default);
    }

    private static bool IsHouseVote(ApiResource? resource) =>
        resource is ApiResource.HouseVoteList or ApiResource.HouseVoteDetail or ApiResource.HouseVoteMembers;

    private static string Detail(string sponsor, string policyArea, int cosponsors, string updateDate = "2025-01-03T12:00:00Z") =>
        $$$"""
        {"bill":{"congress":119,"type":"HR","number":"1","title":"A bill","introducedDate":"2025-01-03",
         "updateDate":"{{{updateDate}}}","policyArea":{"name":"{{{policyArea}}}"},"sponsors":[{"bioguideId":"{{{sponsor}}}"}],
         "cosponsors":{"countIncludingWithdrawnCosponsors":{{{cosponsors}}} },"actions":{"count":1},"summaries":{"count":2} } }
        """;

    private static string Summaries(string? next)
    {
        var nextJson = next is null ? "null" : $"\"{next}\"";
        return $$$"""
            {"summaries":[
             {"versionCode":"173","actionDate":"2025-01-03","actionDesc":"Introduced in House","updateDate":"2025-01-04T10:00:00Z","text":"<p>First</p>"}],
             "pagination":{"next":{{{nextJson}}}}}
            """;
    }

    private static string Cosponsors(string bioguideId, string? next)
    {
        var nextJson = next is null ? "null" : $"\"{next}\"";
        return $$$"""{"cosponsors":[{"bioguideId":"{{{bioguideId}}}","sponsorshipDate":"2025-01-04","isOriginalCosponsor":false}],"pagination":{"next":{{{nextJson}}}}}""";
    }

    private sealed class RecordingBillAdmissions : IBillStreamAdmissionStore
    {
        public List<(string JobId, CongressBill Bill, BillGateOutcome Gate, string IssueRevisionId)> Details { get; } = [];
        public List<(string JobId, CongressBillCosponsorsPage Page)> CosponsorPages { get; } = [];
        public List<(string JobId, CongressBillActionsPage Page)> ActionPages { get; } = [];
        public List<(string JobId, CongressBillSummariesPage Page)> SummaryPages { get; } = [];
        public List<string> ExtractionIds { get; } = [];
        public bool DuplicatesOnly { get; set; }

        public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
            BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken) =>
            Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));

        public Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
            CongressBill bill, BillGateOutcome gate, string issueRevisionId, string extractionId, CancellationToken cancellationToken)
        {
            Details.Add((detailJobId, bill, gate, issueRevisionId));
            ExtractionIds.Add(extractionId);
            return Task.FromResult(DuplicatesOnly
                ? new DiscoveryAdmissionResult([], 0, 2)
                : new DiscoveryAdmissionResult(
                    [new DiscoveryAdmissionJob("a", "https://x"), new DiscoveryAdmissionJob("b", "https://y")], 0, 1));
        }

        public Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(ConfiguredCollectionSource source, string pageJobId,
            CongressBillCosponsorsPage page, string extractionId, CancellationToken cancellationToken)
        {
            CosponsorPages.Add((pageJobId, page));
            ExtractionIds.Add(extractionId);
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitActionsPageAsync(ConfiguredCollectionSource source, string pageJobId,
            CongressBillActionsPage page, string extractionId, CancellationToken cancellationToken)
        {
            ActionPages.Add((pageJobId, page));
            ExtractionIds.Add(extractionId);
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitSummariesPageAsync(ConfiguredCollectionSource source, string pageJobId,
            CongressBillSummariesPage page, string extractionId, CancellationToken cancellationToken)
        {
            SummaryPages.Add((pageJobId, page));
            ExtractionIds.Add(extractionId);
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }
    }

    private sealed class RecordingVoteAdmissions : IVoteAdmissionStore
    {
        public List<(string JobId, HouseVoteListPage Page)> ListPages { get; } = [];
        public List<(string JobId, HouseVote Vote)> Details { get; } = [];
        public List<(string JobId, HouseVote Vote)> Members { get; } = [];
        public List<string> ExtractionIds { get; } = [];
        public List<(string JobId, SenateVoteMenu Menu)> SenateMenus { get; } = [];
        public List<(string JobId, SenateVote Vote, string ExtractionId)> SenateVotes { get; } = [];

        public Task<DiscoveryAdmissionResult> AdmitSenateMenuAsync(ConfiguredCollectionSource source, string menuJobId,
            SenateVoteMenu menu, CancellationToken cancellationToken)
        {
            SenateMenus.Add((menuJobId, menu));
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitSenateVoteAsync(ConfiguredCollectionSource source, string voteJobId,
            SenateVote vote, string extractionId, CancellationToken cancellationToken)
        {
            SenateVotes.Add((voteJobId, vote, extractionId));
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
            HouseVoteListPage page, CancellationToken cancellationToken)
        {
            ListPages.Add((listJobId, page));
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
            HouseVote vote, string extractionId, CancellationToken cancellationToken)
        {
            Details.Add((detailJobId, vote));
            ExtractionIds.Add(extractionId);
            return Task.FromResult(new DiscoveryAdmissionResult([new DiscoveryAdmissionJob("members", "https://x")], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitMembersAsync(ConfiguredCollectionSource source, string membersJobId,
            HouseVote vote, string extractionId, CancellationToken cancellationToken)
        {
            Members.Add((membersJobId, vote));
            ExtractionIds.Add(extractionId);
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }
    }

    private sealed class FakeTextExtractor(string? fragmentText = null) : IDocumentTextExtractor
    {
        public string ExtractionVersion => "fake-extraction";

        public Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, DocumentContentProfile? profile,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ExtractHtmlFragmentAsync(string markup, CancellationToken cancellationToken) =>
            Task.FromResult(fragmentText ?? "text of " + markup);
    }

    private sealed class RecordingExtractions : IDocumentExtractionStore
    {
        public List<DocumentExtraction> Saved { get; } = [];

        public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken)
        {
            Saved.Add(extraction);
            return Task.FromResult(extraction);
        }
    }

    private sealed class ThrowingProcessingStore : IEvidenceProcessingStore
    {
        public int EnsureCalls { get; private set; }

        public Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId,
            string requestedUrl, CancellationToken cancellationToken)
        {
            EnsureCalls++;
            throw new InvalidOperationException("API jobs have no extraction stage.");
        }

        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsPredecessorSettledAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionChangeEvent>> ListRecentChangesAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SingleJob(CollectionJobRecord job) : ICollectionJobStore
    {
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) =>
            Task.FromResult<CollectionJobRecord?>(job);

        public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> RequestCheckAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(IReadOnlyCollection<string> sourceIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult?> ReadPacingBlockAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturedAttempts(string contentType) : ICollectionAttemptStore
    {
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredCollectionAttempt?>(new StoredCollectionAttempt(
                new CapturedAttemptResult(attemptId, "congress-bills", "https://api.congress.gov/v3/bill",
                    "https://api.congress.gov/v3/bill", DateTimeOffset.UnixEpoch,
                    new CollectionResponse(200, null, null, contentType, []), new CaptureIdentity(new string('a', 64), 1)),
                null, null));

        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRoster : CivicLens.Application.Roster.IMemberRosterStore
    {
        public bool Known { get; set; }

        public List<(string AttemptId, IReadOnlyList<CivicLens.Application.Roster.RosterMember> Members)> Admissions { get; } = [];

        public List<MemberPhoto> Photos { get; } = [];

        public Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken) => Task.FromResult(Known);

        public Task AdmitPhotoAsync(MemberPhoto photo, CancellationToken cancellationToken)
        {
            Photos.Add(photo);
            return Task.CompletedTask;
        }

        public List<ConfiguredCollectionSource?> PhotoSourcesOfAdmissions { get; } = [];

        public List<ConfiguredCollectionSource> PhotoJobAdmissions { get; } = [];

        public Task AdmitAsync(string attemptId, IReadOnlyList<CivicLens.Application.Roster.RosterMember> members,
            ConfiguredCollectionSource? photos, CancellationToken cancellationToken)
        {
            Admissions.Add((attemptId, members));
            PhotoSourcesOfAdmissions.Add(photos);
            return Task.CompletedTask;
        }

        public Task AdmitPhotoJobsAsync(ConfiguredCollectionSource photos, CancellationToken cancellationToken)
        {
            PhotoJobAdmissions.Add(photos);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CivicLens.Application.Roster.OfficialPhoto>> ReadNewestPhotosAsync(IReadOnlyCollection<string> personIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CivicLens.Application.Roster.RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CivicLens.Application.Roster.SenatorLisId>> ReadSenatorLisIdsAsync(string sourceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingExecutive : CivicLens.Application.Roster.IExecutiveRosterStore
    {
        public bool Known { get; set; }

        public List<(string AttemptId, IReadOnlyList<CivicLens.Application.Roster.ExecutiveOfficial> Officials)> Admissions { get; } = [];

        public Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken) => Task.FromResult(Known);

        public Task AdmitAsync(string attemptId, IReadOnlyList<CivicLens.Application.Roster.ExecutiveOfficial> officials, CancellationToken cancellationToken)
        {
            Admissions.Add((attemptId, officials));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CivicLens.Application.Roster.ExecutiveOfficial>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoDiscoveryAdmissions : IDiscoveryAdmissionStore
    {
        public Task<DiscoveryAdmissionResult> AdmitAsync(ConfiguredCollectionSource source, string attemptId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DiscoveryAdmissionResult> AdmitAutomaticCheckAsync(ConfiguredCollectionSource source, string attemptId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedBody(byte[] body) : ICaptureBodyReader
    {
        public Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken) =>
            Task.FromResult(body);
    }
}
