using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class HouseVoteFixtures
{
    public const string SourceId = "congress-house-votes";
    public const string SourceUrl = "https://api.congress.gov/v3/house-vote";
    public const string Legislation = "\"legislationNumber\":\"14\",\"legislationType\":\"HCONRES\",";
    public static readonly DateOnly Backfill = new(2025, 1, 3);
    public static readonly VoteSession FirstSession = new(119, 1);
    public static readonly HouseVoteRoll Roll100 = new(FirstSession, 100);
    public static readonly DateTimeOffset Updated = new(2025, 6, 24, 12, 34, 41, TimeSpan.Zero);

    public static CollectionRequest Scope(string url) => new()
    {
        JobId = "job",
        SourceId = SourceId,
        Url = url,
        AllowedOrigin = "https://api.congress.gov",
        AllowedPathPrefix = "/v3/house-vote",
        ArtifactDirectory = Path.GetFullPath("captures"),
        Mode = CollectionMode.Api,
        Credential = CollectionCredentials.CongressGov
    };

    public static HouseVote Vote(int roll, DateTimeOffset updateDate, string? billType = "hconres", int? billNumber = 14) =>
        new(new HouseVoteRoll(FirstSession, roll), updateDate, billType, billNumber);

    public static string Detail(string updateDate = "2025-06-24T08:34:41-04:00", string legislation = Legislation,
        int roll = 100, int session = 1) => $$$"""
        {"houseRollCallVote":{"congress":119,{{{legislation}}}"result":"Passed","rollCallNumber":{{{roll}}},
         "sessionNumber":{{{session}}},"startDate":"2025-04-10T11:08:00-04:00","updateDate":"{{{updateDate}}}",
         "votePartyTotal":[{"nayTotal":2,"party":{"name":"Republican","type":"R"},"voteParty":"R","yeaTotal":216,"presentTotal":0,"notVotingTotal":3},
                           {"nayTotal":212,"party":{"name":"Democrat","type":"D"},"voteParty":"D","yeaTotal":1,"presentTotal":0,"notVotingTotal":2}],
         "voteQuestion":"On Motion to Concur in the Senate Amendment","voteType":"Yea-and-Nay",
         "sourceDataURL":"https://clerk.house.gov/evs/2025/roll{{{roll:000}}}.xml"},
         "request":{"congress":"119","session":"1"}}
        """;

    public static string Members(string updateDate = "2025-06-24T08:34:41-04:00", string legislation = Legislation,
        int roll = 100) => $$$"""
        {"houseRollCallVoteMemberVotes":{"congress":119,{{{legislation}}}"result":"Passed","rollCallNumber":{{{roll}}},
         "sessionNumber":1,"updateDate":"{{{updateDate}}}",
         "results":[{"bioguideID":"A000055","firstName":"Robert","lastName":"Aderholt","voteCast":"Yea","voteParty":"R","voteState":"AL"},
                    {"bioguideID":"A000148","firstName":"Jake","lastName":"Auchincloss","voteCast":"Nay","voteParty":"D","voteState":"MA"},
                    {"bioguideID":"A000369","firstName":"Mark","lastName":"Amodei","voteCast":"Not Voting","voteParty":"R","voteState":"NV"}]},
         "request":{"congress":"119","session":"1"}}
        """;

    public static CollectionConfiguration Configuration(DiscoveryAdmissionPolicy? policy = null) => new()
    {
        Version = 1,
        People =
        [
            new PersonConfiguration { Id = "with-id-a", Name = "A", BioguideId = "A000001" },
            new PersonConfiguration { Id = "without-id", Name = "C" }
        ],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = SourceId,
                Url = SourceUrl,
                AllowedOrigin = "https://api.congress.gov",
                AllowedPathPrefix = "/v3/house-vote",
                Mode = CollectionMode.Api,
                Credential = CollectionCredentials.CongressGov,
                ApiParser = ApiParser.CongressHouseVoteList,
                MinimumCongress = 119,
                BackfillFrom = Backfill,
                AdmissionPolicy = policy ?? new DiscoveryAdmissionPolicy
                {
                    MaxJobs = 251,
                    MaxTotalRequests = 1506,
                    MaxTotalBytes = 753_000_000,
                    MaxTotalTimeoutSeconds = 15_060
                },
                MaxRequests = 2,
                MaxBytes = 1_000_000,
                TimeoutSeconds = 20
            }
        ]
    };
}
