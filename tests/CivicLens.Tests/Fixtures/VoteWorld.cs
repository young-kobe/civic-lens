using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Votes;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Fixtures;

internal sealed record MemberSpec(string Bioguide, string First, string Last, string Cast);

/// <summary>One House roll call with real response bodies, the texts that preparation would have saved, and the stores that serve them.</summary>
internal sealed class VoteWorld
{
    public const string Aderholt = "A000055";
    public const string Auchincloss = "A000148";
    public const string Amodei = "A000369";
    public const string SourceId = HouseVoteFixtures.SourceId;
    public const string BillTitle = "A concurrent resolution on rent";
    private static readonly DateTimeOffset BillUpdatedAt = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly CollectionConfigurationRevision BillRevision =
        CollectionConfigurationRevision.Create(ActionBillWorld.Configuration());
    public static readonly string RecordId = new('a', 64);
    public static readonly string CandidateId = new('b', 64);
    public static readonly DateTimeOffset UpdatedAt = HouseVoteFixtures.Updated;

    private static readonly MemberSpec[] DefaultMembers =
    [
        new(Aderholt, "Robert", "Aderholt", "Yea"),
        new(Auchincloss, "Jake", "Auchincloss", "Nay"),
        new(Amodei, "Mark", "Amodei", "Not Voting")
    ];

    private readonly Dictionary<string, string> bodies = [];
    private readonly Dictionary<string, string> versionOverrides = [];
    private readonly Dictionary<string, CollectionJobRecord> jobs = [];
    private readonly Dictionary<string, string> texts = [];

    private VoteWorld(CollectionConfigurationRevision revision) => Revision = revision;

    public CollectionConfigurationRevision Revision { get; }

    public static CollectionConfiguration Configuration() => HouseVoteFixtures.Configuration() with
    {
        People =
        [
            new() { Id = "aderholt", Name = "Robert Aderholt", BioguideId = Aderholt },
            new() { Id = "auchincloss", Name = "Jake Auchincloss", BioguideId = Auchincloss },
            new() { Id = "pelosi", Name = "Nancy Pelosi", BioguideId = "P000197" },
            new() { Id = "no-bioguide", Name = "Nobody Listed" }
        ]
    };

    public static VoteWorld Create(MemberSpec[]? members = null, string result = "Passed", string? question = null,
        string legislation = HouseVoteFixtures.Legislation, DateOnly? backfillFrom = null, string? totals = null,
        string billTitle = BillTitle, string? policyArea = BillGateFixtures.TrackedPolicyArea, bool withStartDate = true)
    {
        var configuration = Configuration();
        if (backfillFrom is { } from)
            configuration = configuration with { Sources = [configuration.Sources[0] with { BackfillFrom = from }] };
        var world = new VoteWorld(CollectionConfigurationRevision.Create(configuration));
        var detail = HouseVoteFixtures.Detail(legislation: legislation).Replace("\"result\":\"Passed\"", $"\"result\":\"{result}\"", StringComparison.Ordinal);
        if (totals is not null)
            detail = Regex.Replace(detail, @"""votePartyTotal"":\[.*?\}\],", $"\"votePartyTotal\":{totals},", RegexOptions.Singleline);
        if (question is not null)
            detail = detail.Replace("On Motion to Concur in the Senate Amendment", question, StringComparison.Ordinal);
        if (!withStartDate) detail = Regex.Replace(detail, @"""startDate"":""[^""]*"",", "");
        var roll = HouseVoteFixtures.Roll100;
        world.Add("detail", ApiResource.HouseVoteDetail, HouseVoteRollUrl.Detail(HouseVoteFixtures.SourceUrl, roll), detail,
            HouseVoteRenderer.RenderDetail(HouseVoteParser.Parse(Encoding.UTF8.GetBytes(detail), roll)));
        var membersBody = MembersBody(members ?? DefaultMembers);
        world.Add("members", ApiResource.HouseVoteMembers, HouseVoteRollUrl.Members(HouseVoteFixtures.SourceUrl, roll), membersBody,
            HouseVoteRenderer.RenderMembers(HouseVoteMembersParser.Parse(Encoding.UTF8.GetBytes(membersBody), roll)));
        var bill = new CongressBill(119, "hconres", 14, billTitle, ActionBillWorld.Backfill, BillUpdatedAt, policyArea, [], 0, 1, 0);
        var billBody = ActionBillWorld.DetailBody(bill);
        var billUrl = $"{ActionBillWorld.SourceUrl}/119/hconres/14?format=json";
        world.Add("bill", ApiResource.Bill, billUrl, billBody,
            CongressBillRenderer.RenderDetail(CongressBillParser.Parse(Encoding.UTF8.GetBytes(billBody), CongressBillListItem.TryReadDetailUrl(ActionBillWorld.SourceUrl, billUrl)!)),
            BillRevision, ActionBillWorld.SourceId, ActionBillWorld.Backfill);
        world.Stores = new ActionBillWorld.FixtureStores(world.jobs, world.bodies);
        return world;
    }

    public ActionBillWorld.FixtureStores Stores { get; private set; } = null!;

    public VoteReader Reader() => new(Stores, new ApiResponseReader(Stores, Stores), "artifacts");

    public VoteDraftEvidence Evidence(VoteDraftRevision? current = null, DateTimeOffset? updatedAt = null, bool hasNewerCandidate = false) =>
        new(new VoteDraftCandidate(CandidateId, RecordId, SourceId, 119, 1, 100, "hconres", 14, updatedAt ?? UpdatedAt), Revision,
            Page("detail", HouseVoteParser.Version), Page("members", HouseVoteMembersParser.Version), BillPage(), current, hasNewerCandidate);

    public static string ExtractionIdOf(string jobId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));

    public string Quote(SourceSpan span)
    {
        var jobId = texts.Keys.Single(job => ExtractionIdOf(job) == span.ExtractionId);
        return texts[jobId].Substring(span.Start, span.Length);
    }

    public void SetVersion(string jobId, string parserVersion) => versionOverrides[jobId] = parserVersion;

    public void TamperWithSavedText(string jobId, string text) => texts[jobId] = text;

    public void ReplaceCapturedBody(string jobId, string body) => bodies["attempt-" + jobId] = body;

    private VoteDraftPage BillPage() =>
        new("bill", ExtractionIdOf("bill"), versionOverrides.GetValueOrDefault("bill") ?? CongressBillParser.Version,
            CongressBillRenderer.NormalizationVersion, texts["bill"]);

    private VoteDraftPage Page(string jobId, string parserVersion) =>
        new(jobId, ExtractionIdOf(jobId), versionOverrides.GetValueOrDefault(jobId) ?? parserVersion,
            HouseVoteRenderer.NormalizationVersion, texts[jobId]);

    private static string MembersBody(MemberSpec[] members) => JsonSerializer.Serialize(new
    {
        houseRollCallVoteMemberVotes = new
        {
            congress = 119,
            legislationNumber = "14",
            legislationType = "HCONRES",
            result = "Passed",
            rollCallNumber = 100,
            sessionNumber = 1,
            updateDate = "2025-06-24T08:34:41-04:00",
            results = members.Select(member => new
            {
                bioguideID = member.Bioguide,
                firstName = member.First,
                lastName = member.Last,
                voteCast = member.Cast,
                voteParty = "R",
                voteState = "AL"
            })
        },
        request = new { congress = "119", session = "1" }
    });

    private void Add(string jobId, ApiResource resource, string url, string body, string renderedText)
    {
        var coveredFrom = Revision.Configuration.Sources.Single().BackfillFrom!.Value;
        Add(jobId, resource, url, body, renderedText, Revision, SourceId, coveredFrom);
    }

    private void Add(string jobId, ApiResource resource, string url, string body, string renderedText,
        CollectionConfigurationRevision revision, string sourceId, DateOnly coveredFrom)
    {
        var configured = new ConfiguredCollectionSource(revision, sourceId, coveredFrom);
        var definition = configured.CreateApiJobDefinition(resource, url, coveredFrom);
        var attempt = new CollectionJobAttempt("attempt-" + jobId, definition.CreateRequest(jobId, Path.GetFullPath("captures")),
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        jobs[jobId] = new CollectionJobRecord(jobId, definition, "key-" + jobId, CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null,
            false, [attempt]);
        bodies["attempt-" + jobId] = body;
        texts[jobId] = renderedText;
    }
}
