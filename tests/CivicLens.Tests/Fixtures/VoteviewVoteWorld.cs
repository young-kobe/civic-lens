using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Application.Roster;
using CivicLens.Application.Votes;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Fixtures;

/// <summary>Roll call 60 of the trimmed Voteview files, with the text that preparation would have saved and the stores that serve the three captures.</summary>
internal sealed class VoteviewVoteWorld
{
    public const string King = "K000383";
    public const string Coons = "C001088";
    public const string RollCallsJob = "rolls";
    public const string MembersJob = "members";
    public const string VotesJob = "votes";
    public static readonly string RecordId = new('c', 64);
    public static readonly string CandidateId = new('d', 64);
    public static readonly DateTimeOffset UpdatedAt = new(2026, 10, 29, 6, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string> bodies = [];
    private readonly Dictionary<string, CollectionJobRecord> jobs = [];

    private VoteviewVoteWorld(string rollCalls, string members, string votes)
    {
        Revision = CollectionConfigurationRevision.Create(Configuration());
        Add(RollCallsJob, ApiResource.VoteviewRollCalls, rollCalls);
        Add(MembersJob, ApiResource.VoteviewMembers, members);
        Add(VotesJob, ApiResource.VoteviewVotes, votes);
        Stores = new ActionBillWorld.FixtureStores(jobs, bodies);
        Text = VoteviewRenderer.Render(VoteviewRoll.Join(VoteviewParser.ParseRollCalls(Encoding.UTF8.GetBytes(rollCalls), 119),
            VoteviewParser.ParseCasts(Encoding.UTF8.GetBytes(votes), 119), VoteviewParser.ParseMembers(Encoding.UTF8.GetBytes(members), 119))
            .Single(roll => roll.RollCall.Part == 60));
    }

    public CollectionConfigurationRevision Revision { get; }

    public ActionBillWorld.FixtureStores Stores { get; }

    public string Text { get; }

    public static CollectionConfiguration Configuration() => VoteviewVoteFixtures.Configuration() with
    {
        People =
        [
            new() { Id = "king", Name = "Angus King", BioguideId = King },
            new() { Id = "coons", Name = "Chris Coons", BioguideId = Coons }
        ]
    };

    public static VoteviewVoteWorld Create() =>
        new(VoteviewVoteFixtures.DefaultRollCalls, VoteviewVoteFixtures.DefaultMembers, VoteviewVoteFixtures.DefaultVotes);

    public VoteReader Reader() => new(Stores, new FixedMemberRoster([], []), new ApiResponseReader(Stores, Stores), "artifacts");

    public VoteDraftEvidence Evidence(string? savedText = null, string extractionVersion = VoteviewParser.ExtractionVersion, string? rosterJobId = MembersJob,
        int rollCall = 60)
    {
        var page = Page(VotesJob, savedText ?? Text, extractionVersion);
        return new(new VoteDraftCandidate(CandidateId, RecordId, VoteviewVoteFixtures.SourceId, Chamber.Senate, 119, 1, rollCall, null, null, UpdatedAt, rosterJobId),
            Revision, page with { JobId = RollCallsJob }, page, null, null, false);
    }

    public string Quote(SourceSpan span) => Text.Substring(span.Start, span.Length);

    public static string ExtractionIdOf(string jobId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));

    private static VoteDraftPage Page(string jobId, string text, string extractionVersion) =>
        new(jobId, ExtractionIdOf(jobId), extractionVersion, text);

    private void Add(string jobId, ApiResource resource, string body)
    {
        var configured = new ConfiguredCollectionSource(Revision, VoteviewVoteFixtures.SourceId, VoteviewVoteFixtures.Backfill);
        var url = VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, resource, 119);
        var definition = configured.CreateApiJobDefinition(resource, url, VoteviewVoteFixtures.Backfill, CollectionJobPriority.Check);
        var attempt = new CollectionJobAttempt("attempt-" + jobId, definition.CreateRequest(jobId, Path.GetFullPath("captures")),
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        jobs[jobId] = new CollectionJobRecord(jobId, definition, "key-" + jobId, CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null,
            false, [attempt]);
        bodies["attempt-" + jobId] = body;
    }
}
