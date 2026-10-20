using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class ExecutiveFixtures
{
    public const string SourceId = "executive-officials";
    public const string SourceUrl = "https://unitedstates.github.io/congress-legislators/executive.json";

    // Trimmed excerpt of executive.json from github.com/unitedstates/congress-legislators (CC0), fetched 2026-10-19.
    // Washington has no wikidata ID. Trump has no bioguide ID and no official_full name. Biden held both offices.
    public const string Json = """
        [
         {"id":{"bioguide":"W000178","govtrack":411351},
          "name":{"first":"George","last":"Washington"},
          "terms":[
           {"type":"prez","start":"1789-04-30","end":"1793-03-04","party":"no party","how":"election"},
           {"type":"prez","start":"1793-03-04","end":"1797-03-04","party":"no party","how":"election"}]},
         {"id":{"bioguide":"B000444","govtrack":300008,"wikidata":"Q6279"},
          "name":{"first":"Joseph","middle":"Robinette","last":"Biden","suffix":"Jr.","nickname":"Joe"},
          "terms":[
           {"type":"viceprez","start":"2009-01-20","end":"2013-01-20","party":"Democrat","how":"election"},
           {"type":"viceprez","start":"2013-01-20","end":"2017-01-20","party":"Democrat","how":"election"},
           {"type":"prez","start":"2021-01-20","end":"2025-01-20","party":"Democrat","how":"election"}]},
         {"id":{"govtrack":412733,"wikidata":"Q22686"},
          "name":{"first":"Donald","middle":"J.","last":"Trump"},
          "terms":[
           {"type":"prez","start":"2017-01-20","end":"2021-01-20","party":"Republican","how":"election"},
           {"type":"prez","start":"2025-01-20","end":"2029-01-20","party":"Republican","how":"election"}]}
        ]
        """;

    public static ExecutiveOfficial[] Officials() => ExecutiveRosterParser.Parse(System.Text.Encoding.UTF8.GetBytes(Json));

    public static WatchedSourceConfiguration Source() => RosterFixtures.Source() with
    {
        Id = SourceId,
        Url = SourceUrl,
        AllowedPathPrefix = "/congress-legislators/executive.json",
        ApiParser = ApiParser.CongressLegislatorsExecutive,
        MaxBytes = 200_000
    };

    public static CollectionConfiguration Configuration() => RosterFixtures.Configuration() with { Sources = [Source()] };
}
