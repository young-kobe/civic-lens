using CivicLens.Core.Briefs;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class PublishedBriefContentSeed
{
    public static async Task SeedAsync(string connectionString)
    {
        var seeder = new BriefWorldSeeder(connectionString);
        await seeder.StatementAsync(BriefFixtures.CitedStatement, 1, "Pinned statement headline");
        await seeder.StatementAsync(BriefWorldSeeder.StatementId('3'), 1, "Suggestion target headline");
        await seeder.ActionAsync(BriefFixtures.CitedAction, 1, 7);
        await seeder.DocumentChangeAsync(BriefFixtures.CitedDocumentChange, 1, "Pinned document headline");
        var revision = BriefFixtures.ReviewableRevision() with
        {
            Headline = "Published brief headline",
            Paragraphs = [new("Text paragraph one", ["R1"]), new("Text paragraph two", ["R2", "R3"])],
            Suggestion = new("Suggestion headline", [new("Suggestion paragraph", ["R1"])],
                [new("R1", BriefCitationKind.Statement, BriefWorldSeeder.StatementId('3'), 1)], new('a', 32), new('b', 64))
        };
        await seeder.ReviewableBriefAsync(revision);
        await seeder.ReleaseAsync(1, BriefFixtures.InWeek, true,
            BriefWorldSeeder.Binding(PublishedRecordKind.Brief, BriefFixtures.RecordId, 1),
            BriefWorldSeeder.Binding(PublishedRecordKind.Statement, BriefFixtures.CitedStatement, 1),
            BriefWorldSeeder.Binding(PublishedRecordKind.Statement, BriefWorldSeeder.StatementId('3'), 1),
            BriefWorldSeeder.Binding(PublishedRecordKind.Action, BriefFixtures.CitedAction, 1),
            BriefWorldSeeder.Binding(PublishedRecordKind.DocumentChange, BriefFixtures.CitedDocumentChange, 1));
        await seeder.AppendRevisionAsync(revision with { RevisionNumber = 2, Headline = "Unpublished brief revision" });
        await seeder.StatementAsync(BriefFixtures.CitedStatement, 2, "New unpublished statement headline");
        await seeder.ActionAsync(BriefFixtures.CitedAction, 2, 7);
        await seeder.DocumentChangeAsync(BriefFixtures.CitedDocumentChange, 2, "New unpublished document headline");
    }
}
