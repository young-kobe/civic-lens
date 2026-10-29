using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

/// <summary>
/// Trimmed copies of the Voteview S119 files fetched 2026-10-29 from https://voteview.com/static/data/out. They keep roll calls 1, 2, and 60 and six
/// senators. Roll calls 1 and 60 have yea and nay counts changed to the kept votes. Roll call 2 keeps the full Senate counts, so it disagrees with its
/// votes like a damaged file. The cast of the President (icpsr 99912) on roll call 1 is added because the first President casts in the file start at roll call 13.
/// </summary>
internal static class VoteviewVoteFixtures
{
    public const string SourceId = "voteview-senate-votes";
    public const string SourceUrl = "https://voteview.com/static/data/out";
    public static readonly DateOnly Backfill = new(2025, 1, 3);

    public const string RollCallsHeader = "congress,chamber,rollnumber,date,session,clerk_rollnumber,majority_requirement,yea_count,nay_count," +
        "nominate_mid_1,nominate_mid_2,nominate_spread_1,nominate_spread_2,nominate_log_likelihood,bill_number,vote_result,vote_desc,vote_question,dtl_desc";

    public const string DefaultRollCalls = RollCallsHeader + "\n" +
        "119,Senate,1,2025-01-09,1,1,3/5,4,2,-0.444,-0.634,-0.708,-0.181,-10.067,S5,Cloture Motion Agreed to,\"A bill to require the Secretary of Homeland Security to take into custody aliens who have been charged in the United States with theft, and for other purposes.\",On the Cloture Motion,\n" +
        "119,Senate,2,2025-01-13,1,2,1/2,82,10,-0.446,-0.606,-0.715,-0.072,-12.276,S5,Motion to Proceed Agreed to,\"A bill to require the Secretary of Homeland Security to take into custody aliens who have been charged in the United States with theft, and for other purposes.\",On the Motion to Proceed,\n" +
        "119,Senate,60,2025-02-20,1,60,1/2,2,4,0.129,-0.234,-0.647,-0.714,-0.563,PN1235,Cloture Motion Agreed to,\"Kashyap Patel, of Nevada, to be Director of the Federal Bureau of Investigation for a term of ten years\",On the Cloture Motion,\n";

    public const string VotesHeader = "congress,chamber,rollnumber,icpsr,cast_code,prob";

    public const string DefaultVotes = VotesHeader + "\n" +
        "119,Senate,1,14226,1,100.0\n119,Senate,1,14435,6,81.0\n119,Senate,1,29147,6,88.8\n119,Senate,1,40916,1,99.7\n119,Senate,1,41300,1,100.0\n119,Senate,1,42102,1,100.0\n" +
        "119,Senate,1,99912,1,100.0\n" +
        "119,Senate,2,14226,1,100.0\n119,Senate,2,14435,6,82.2\n119,Senate,2,29147,6,89.9\n119,Senate,2,40916,1,99.6\n119,Senate,2,41300,1,100.0\n119,Senate,2,42102,1,100.0\n" +
        "119,Senate,60,14226,1,99.9\n119,Senate,60,14435,6,100.0\n119,Senate,60,29147,6,100.0\n119,Senate,60,40916,6,100.0\n119,Senate,60,41300,6,99.9\n119,Senate,60,42102,1,100.0\n";

    public const string MembersHeader = "congress,chamber,icpsr,state_icpsr,district_code,state_abbrev,party_code,occupancy,last_means,bioname,bioguide_id," +
        "born,died,nominate_dim1,nominate_dim2,nominate_log_likelihood,nominate_geo_mean_probability,nominate_number_of_votes,nominate_number_of_errors," +
        "conditional,nokken_poole_dim1,nokken_poole_dim2";

    public const string PresidentRow = "119,President,99912,99,0,USA,200,0,0,\"TRUMP, Donald John\",,1946.0,,0.403,0.162,,,,,,,\n";

    public const string DefaultMembers = MembersHeader + "\n" + PresidentRow +
        "119,Senate,42102,41,0,AL,200,,,\"TUBERVILLE, Thomas Hawley (Tommy)\",T000278,1954.0,,0.936,-0.352,-34.17365,0.9605,848,10,,0.936,-0.352\n" +
        "119,Senate,40916,11,0,DE,100,,,\"COONS, Christopher A.\",C001088,1963.0,,-0.24,-0.206,-90.07506,0.90012,856,29,,-0.262,-0.167\n" +
        "119,Senate,14226,31,0,IA,200,,,\"GRASSLEY, Charles Ernest\",G000386,1933.0,,0.363,-0.034,-26.23035,0.9708,885,10,,0.461,0.16\n" +
        "119,Senate,41300,2,0,ME,328,,,\"KING, Angus Stanley, Jr.\",K000383,1944.0,,-0.17,-0.179,-131.48458,0.86323,894,50,,-0.18,-0.159\n" +
        "119,Senate,14435,3,0,MA,100,,,\"MARKEY, Edward John\",M000133,1946.0,,-0.518,-0.47,-23.20081,0.97458,901,4,,-0.751,-0.524\n" +
        "119,Senate,29147,6,0,VT,328,,,\"SANDERS, Bernard\",S000033,1941.0,,-0.546,-0.429,-28.38735,0.96693,844,9,,-0.704,-0.518\n";

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public static CollectionConfiguration Configuration(bool enabled = true) => new()
    {
        Version = 1,
        People = [new PersonConfiguration { Id = "with-id-a", Name = "A", BioguideId = "A000001" }],
        Sources = [Source(SourceId, enabled)]
    };

    public static WatchedSourceConfiguration Source(string id, bool enabled, string url = SourceUrl) => new()
    {
        Id = id,
        Url = url,
        AllowedOrigin = "https://voteview.com",
        AllowedPathPrefix = "/static/data/out",
        Mode = CollectionMode.Api,
        ApiParser = ApiParser.VoteviewSenateVoteList,
        MinimumCongress = 119,
        BackfillFrom = Backfill,
        Enabled = enabled,
        AdmissionPolicy = new DiscoveryAdmissionPolicy(),
        JobPolicy = new CollectionJobPolicy { MaxAttempts = 2 },
        MaxRequests = 2,
        MaxBytes = 4_000_000,
        TimeoutSeconds = 30
    };
}
