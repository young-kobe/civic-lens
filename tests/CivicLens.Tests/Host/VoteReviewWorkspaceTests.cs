using System.Net;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres) { Issues = StatementGateFixtures.Configuration() };

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task AnUnknownOrMalformedVoteRecordIsNotFound()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        foreach (var id in new[] { new string('a', 64), "not-a-record" })
        {
            using var missing = await host.Client.GetAsync($"/Review/Votes/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task TheVotePageShowsTheTrackedMembersCastAndTheKindWithTheirCitedLines()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync();

        var page = await host.GetDocumentAsync($"/Review/Votes/{recordId}");

        var members = page.QuerySelector("[aria-labelledby='members']")!.TextContent;
        Assert.Contains("A: Yea", members, StringComparison.Ordinal);
        Assert.Contains("Member: A000001, A, R, AL, Yea", members, StringComparison.Ordinal);
        var evidence = page.QuerySelector("[aria-labelledby='evidence']")!.TextContent;
        Assert.Contains("Vote kind: Passage", evidence, StringComparison.Ordinal);
        Assert.Contains("Question: On Passage", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVotePageLinksTheActionDraftOfItsBillAndTheRollCallOnTheClerkSite()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync(billNumber: 1234);
        await new RelationshipWorldSeeder(host.ConnectionString).ActionAsync(RelationshipFixtures.Action(1234), "AwaitingReview");

        var page = await host.GetDocumentAsync($"/Review/Votes/{recordId}");

        var links = page.QuerySelectorAll("[aria-labelledby='evidence'] a").ToDictionary(link => link.TextContent, link => link);
        Assert.Equal($"/Review/Actions/{RelationshipFixtures.ActionId}", links["Action draft of this bill"].GetAttribute("href"));
        Assert.Equal("https://clerk.house.gov/Votes/2025100", links["Roll call on clerk.house.gov"].GetAttribute("href"));
        Assert.Equal("noopener noreferrer", links["Roll call on clerk.house.gov"].GetAttribute("rel"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1234)]
    public async Task TheVotePageHasNoActionLinkWhenTheVoteNamesNoBillOrTheBillHasNoActionDraft(int? billNumber)
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync(billNumber: billNumber);
        await new RelationshipWorldSeeder(host.ConnectionString).ActionAsync(RelationshipFixtures.Action(99), "AwaitingReview");

        var page = await host.GetDocumentAsync($"/Review/Votes/{recordId}");

        Assert.Equal(["Roll call on clerk.house.gov"], page.QuerySelectorAll("[aria-labelledby='evidence'] a").Select(link => link.TextContent));
    }

    [Fact]
    public async Task TheClerkLinkNamesTheCalendarYearOfTheSessionAndWritesTheRollNumberWithoutPadding()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync(session: 2, roll: 5);

        var page = await host.GetDocumentAsync($"/Review/Votes/{recordId}");

        Assert.Equal("https://clerk.house.gov/Votes/20265",
            page.QuerySelector("[aria-labelledby='evidence'] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ASenateVotePageLinksTheSenateRollCallAndNamesTheSenateInItsHints()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        var recordId = await SeedVoteDraftAsync(roll: 5, chamber: Chamber.Senate, kind: VoteKind.MotionToProceed);

        var page = await host.GetDocumentAsync($"/Review/Votes/{recordId}");

        var link = page.QuerySelector("[aria-labelledby='evidence'] a")!;
        Assert.Equal(("Roll call on senate.gov", "https://www.senate.gov/legislative/LIS/roll_call_votes/vote1191/vote_119_1_00005.htm"),
            (link.TextContent, link.GetAttribute("href")));
        Assert.Contains("Vote kind: Motion to proceed", page.QuerySelector("[aria-labelledby='evidence']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("saved Senate vote text", page.QuerySelector("[aria-labelledby='evidence']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Say what the Senate voted on.", page.Body!.InnerHtml, StringComparison.Ordinal);
    }

    private async Task<string> SeedVoteDraftAsync(int session = 1, int roll = 100, int? billNumber = null, Chamber chamber = Chamber.House, VoteKind kind = VoteKind.Passage)
    {
        const string question = "Question: On Passage";
        const string member = "Member: A000001, A, R, AL, Yea";
        var extraction = await host.SaveExtractionAsync($"{question}\n{member}\n");
        var configuration = CollectionConfigurationRevision.Create(HouseVoteFixtures.Configuration());
        var recordId = new string('a', 64);
        var candidateId = new string('b', 64);
        var revision = new VoteDraftRevision(recordId, 1, "auth0|editor", DateTimeOffset.UtcNow, "The House passed a measure",
            "The House voted on passage.", ReviewTextOrigin.Person, null, HouseVoteFixtures.SourceId, chamber == Chamber.Senate ? VoteSource.SenateGov : VoteSource.CongressGov, chamber, 119, session, roll, billNumber is null ? null : "hr", billNumber, candidateId,
            HouseVoteFixtures.Updated, [new(question, new SourceSpan(extraction.ExtractionId, 0, question.Length))],
            new DateOnly(2025, 4, 10), new SourceSpan(extraction.ExtractionId, 0, question.Length), kind,
            new SourceSpan(extraction.ExtractionId, 0, question.Length),
            [new("with-id-a", "A000001", "Yea", new SourceSpan(extraction.ExtractionId, question.Length + 1, member.Length))], [], new string('e', 64));
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SET session_replication_role = replica;
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at) VALUES ('{configuration.Id}', @configuration, 1);
            INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks,
                detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
            VALUES ('{candidateId}', '{HouseVoteFixtures.SourceId}', '{chamber}', 119, {session}, {roll}, 1, 'detail', 'members', '{extraction.ExtractionId}',
                '{extraction.ExtractionId}', '{configuration.Id}', 1);
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number,
                review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', '{chamber}', 119, {session}, {roll}, 1, 0, 'AwaitingReview', 1, 1);
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{recordId}', 1, 'auth0|editor', 'Person', NULL, @revision, 1);
            """, connection);
        command.Parameters.AddWithValue("configuration", configuration.Json);
        command.Parameters.AddWithValue("revision", JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await command.ExecuteNonQueryAsync();
        return recordId;
    }
}
