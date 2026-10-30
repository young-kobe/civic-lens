using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Fixtures;

internal static class RosterFixtures
{
    public const string SourceId = "congress-members";
    public const string SourceUrl = "https://unitedstates.github.io/congress-legislators/legislators-current.json";

    // Trimmed excerpt of legislators-current.json from github.com/unitedstates/congress-legislators (CC0), fetched 2026-10-19.
    // Cantwell moved from the House to the Senate. Norton is a delegate with district 0.
    // Wahab has no official_full name.
    public const string Json = """
        [
         {"id":{"bioguide":"C000127"},
          "name":{"first":"Maria","last":"Cantwell","official_full":"Maria Cantwell"},
          "terms":[
           {"type":"rep","start":"1993-01-05","end":"1995-01-03","state":"WA","district":1,"party":"Democrat"},
           {"type":"sen","start":"2025-01-03","end":"2031-01-03","state":"WA","class":1,"party":"Democrat",
            "url":"https://www.cantwell.senate.gov","office":"511 Hart Senate Office Building","phone":"202-224-3441"}]},
         {"id":{"bioguide":"N000147"},
          "name":{"first":"Eleanor","last":"Norton","official_full":"Eleanor Holmes Norton"},
          "terms":[
           {"type":"rep","start":"1991-01-03","end":"1993-01-03","state":"DC","district":0,"party":"Democrat"},
           {"type":"rep","start":"2025-01-03","end":"2027-01-03","state":"DC","district":0,"party":"Democrat",
            "url":"https://norton.house.gov","office":"2136 Rayburn House Office Building","phone":"202-225-8050"}]},
         {"id":{"bioguide":"W000832"},
          "name":{"first":"Aisha","last":"Wahab"},
          "terms":[
           {"type":"rep","start":"2026-09-02","end":"2027-01-03","state":"CA","district":14,"party":"Democrat",
            "url":"https://wahab.house.gov/","office":"174 Cannon House Office Building","phone":"202-225-5065"}]}
        ]
        """;

    public static RosterMember[] Members() => MemberRosterParser.Parse(System.Text.Encoding.UTF8.GetBytes(Json));

    public static WatchedSourceConfiguration Source() => new()
    {
        Id = SourceId,
        Url = SourceUrl,
        AllowedOrigin = "https://unitedstates.github.io",
        AllowedPathPrefix = "/congress-legislators/legislators-current.json",
        Mode = CollectionMode.Api,
        ApiParser = ApiParser.CongressLegislators,
        CheckIntervalSeconds = 86400,
        MaxRequests = 2,
        MaxBytes = 2_500_000,
        TimeoutSeconds = 30
    };

    public static CollectionConfiguration Configuration(params PersonConfiguration[] people) => new()
    {
        Version = 1,
        People = people,
        Sources = [Source()]
    };

    public static PersonConfiguration Person(string id, string name, string? bioguideId) =>
        new() { Id = id, Name = name, BioguideId = bioguideId };

    public static async Task<string> ImportCaptureAsync(PostgresCollectionAttemptStore attempts, string captureSha256,
        DateTimeOffset observedAt, string sourceId = SourceId)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        var request = Request() with
        {
            SourceId = sourceId,
            Url = SourceUrl,
            AllowedOrigin = "https://unitedstates.github.io",
            AllowedPathPrefix = "/congress-legislators"
        };
        var result = Receipt(request) with
        {
            ObservedAt = observedAt,
            Capture = new CaptureArtifact { Sha256 = captureSha256, RelativePath = captureSha256 + ".gz", ByteLength = 4 }
        };
        await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, result), CancellationToken.None);
        return attemptId;
    }
}
