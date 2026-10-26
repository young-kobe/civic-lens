using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Roster;
using CivicLens.Application.Votes;
using CivicLens.Core.Collection;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Fixtures;

/// <summary>One Senate roll call with its real file shape, the text that preparation would have saved, and the stores that serve them.</summary>
internal sealed class SenateVoteWorld
{
    public const string Alsobrooks = "A000382";
    public const string Banks = "B001299";
    public const string AlsobrooksLis = "S428";
    public const string BanksLis = "S429";
    public const string NominationDocument = """
        <document>
          <document_congress>119</document_congress>
          <document_type>PN</document_type>
          <document_number>373</document_number>
          <document_name>PN373</document_name>
          <document_title>Sara Bailey, of Texas, to be Director of National Drug Control Policy</document_title>
          <document_short_title/>
        </document>
        """;

    public static readonly string RecordId = new('c', 64);
    public static readonly string CandidateId = new('d', 64);
    public static readonly DateTimeOffset UpdatedAt = new(2025, 1, 10, 19, 57, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BillUpdatedAt = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly CollectionConfigurationRevision BillRevision = CollectionConfigurationRevision.Create(ActionBillWorld.Configuration());

    private readonly Dictionary<string, string> bodies = [];
    private readonly Dictionary<string, CollectionJobRecord> jobs = [];
    private readonly Dictionary<string, string> texts = [];
    private readonly SenateVoteRoll roll = SenateVoteFixtures.Roll1;

    private SenateVoteWorld(CollectionConfigurationRevision revision, bool hasBill)
    {
        Revision = revision;
        HasBill = hasBill;
    }

    public CollectionConfigurationRevision Revision { get; }

    public bool HasBill { get; }

    public ActionBillWorld.FixtureStores Stores { get; private set; } = null!;

    public static CollectionConfiguration Configuration() => SenateVoteFixtures.Configuration() with
    {
        People =
        [
            new() { Id = "alsobrooks", Name = "Angela Alsobrooks", BioguideId = Alsobrooks },
            new() { Id = "banks", Name = "Jim Banks", BioguideId = Banks },
            new() { Id = "no-bioguide", Name = "Nobody Listed" }
        ],
        Sources = [SenateVoteFixtures.Configuration().Sources[0], RosterFixtures.Source()]
    };

    public static SenatorLisId[] Senators() => [new(Alsobrooks, AlsobrooksLis), new(Banks, BanksLis)];

    public static SenateVoteWorld Create(string question = "On Cloture on the Motion to Proceed", string? document = null, string members = SenateVoteFixtures.DefaultMembers,
        string count = SenateVoteFixtures.DefaultCount, string? policyArea = BillGateFixtures.TrackedPolicyArea)
    {
        var hasBill = document is null;
        var world = new SenateVoteWorld(CollectionConfigurationRevision.Create(Configuration()), hasBill);
        var body = SenateVoteFixtures.Vote(question: question, document: document ?? SenateVoteFixtures.DefaultDocument, members: members, count: count);
        var url = SenateVoteUrl.Vote(SenateVoteFixtures.SourceUrl, world.roll);
        world.Add("vote", ApiResource.SenateVote, url, Encoding.UTF8.GetString(body),
            SenateVoteRenderer.Render(SenateVoteParser.Parse(body, world.roll)), world.Revision, SenateVoteFixtures.SourceId, SenateVoteFixtures.Backfill);
        if (hasBill)
        {
            var bill = new CongressBill(119, "s", 5, "A bill on custody", ActionBillWorld.Backfill, BillUpdatedAt, policyArea, [], 0, 1, 0);
            var billBody = ActionBillWorld.DetailBody(bill);
            var billUrl = $"{ActionBillWorld.SourceUrl}/119/s/5?format=json";
            world.Add("bill", ApiResource.Bill, billUrl, billBody,
                CongressBillRenderer.RenderDetail(CongressBillParser.Parse(Encoding.UTF8.GetBytes(billBody), CongressBillListItem.TryReadDetailUrl(ActionBillWorld.SourceUrl, billUrl)!)),
                BillRevision, ActionBillWorld.SourceId, ActionBillWorld.Backfill);
        }
        world.Stores = new ActionBillWorld.FixtureStores(world.jobs, world.bodies);
        return world;
    }

    public VoteReader Reader(params SenatorLisId[] senators) =>
        new(Stores, new FixedMemberRoster([], senators), new ApiResponseReader(Stores, Stores), "artifacts");

    public VoteDraftEvidence Evidence(string versionOverride = "")
    {
        var vote = Page("vote", string.IsNullOrEmpty(versionOverride) ? SenateVoteParser.ExtractionVersion : versionOverride);
        return new(new VoteDraftCandidate(CandidateId, RecordId, SenateVoteFixtures.SourceId, Chamber.Senate, 119, 1, 1,
                HasBill ? "s" : null, HasBill ? 5 : null, UpdatedAt),
            Revision, vote, vote, HasBill ? BillPage() : null, null, false);
    }

    public static string ExtractionIdOf(string jobId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));

    public string Quote(CivicLens.Core.Documents.SourceSpan span)
    {
        var jobId = texts.Keys.Single(job => ExtractionIdOf(job) == span.ExtractionId);
        return texts[jobId].Substring(span.Start, span.Length);
    }

    private VoteDraftPage BillPage() =>
        new("bill", ExtractionIdOf("bill"), CongressBillParser.ExtractionVersion, texts["bill"]);

    private VoteDraftPage Page(string jobId, string extractionVersion) =>
        new(jobId, ExtractionIdOf(jobId), extractionVersion, texts[jobId]);

    private void Add(string jobId, ApiResource resource, string url, string body, string renderedText,
        CollectionConfigurationRevision revision, string sourceId, DateOnly coveredFrom)
    {
        var configured = new ConfiguredCollectionSource(revision, sourceId, coveredFrom);
        var definition = configured.CreateApiJobDefinition(resource, url, coveredFrom, CollectionJobPriority.Check);
        var attempt = new CollectionJobAttempt("attempt-" + jobId, definition.CreateRequest(jobId, Path.GetFullPath("captures")),
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        jobs[jobId] = new CollectionJobRecord(jobId, definition, "key-" + jobId, CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null,
            false, [attempt]);
        bodies["attempt-" + jobId] = body;
        texts[jobId] = renderedText;
    }
}
