using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>The migration that serves the exact quotes of the bill text sentences and key terms. It and <c>ActionRevisionKeyTerms</c> each hold a
/// copy of <see cref="BillTextWords.KeyTermsLabel"/>, because SQL cannot read the class. The test compares both copies with the owner.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiBillTextQuotesMigrationTests(PostgresCollection postgres)
{
    private const string PreviousMigration = "20261113202000_PublicApiExecutiveActionEvidence";
    private const string Quote = "The term “grant” means a payment under § 3.";

    [Fact]
    public async Task EveryCopyOfTheKeyTermLabelEqualsTheOwnerAndTheRollbackKeepsThePublicReadOfTheOldKeyTermView()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            await BillsSeed.AddBillsAsync(database, new SeededBill('a', "2026-03-02")
            {
                Text = new SeededBillText(100, 100, [("grant", "The term \"grant\" means a payment under § 3.", false, Quote)])
            });
            Assert.Equal([$"{BillTextWords.KeyTermsLabel}|{Quote}"], await ViewsSeed.QueryAsPublicAsync(database, "SELECT label || '|' || quote FROM public_api.bill_key_terms"));

            await MigrateAsync(database, PreviousMigration);
            Assert.Equal([BillTextWords.KeyTermsLabel], await LabelsAsync(database));
            Assert.Equal(["false|false"], await ViewsSeed.QueryAsync(database, """
                SELECT (to_regclass('public_api.bill_text_sentences') IS NOT NULL)::text || '|'
                    || EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'bill_key_terms' AND column_name = 'quote')::text
                """));

            // The merged migration keeps its own copy of the label. The test rebuilds the view from that copy and compares it with the owner.
            var keyTermsView = new ActionRevisionKeyTerms().UpOperations.OfType<SqlOperation>()
                .Single(operation => operation.Sql.Contains("CREATE VIEW public_api.bill_key_terms", StringComparison.Ordinal));
            await ViewsSeed.ExecuteAsync(database, $"""
                DROP VIEW public_api.bill_key_terms;
                {keyTermsView.Sql}
                GRANT SELECT ON public_api.bill_key_terms TO civic_lens_public;
                """);
            Assert.Equal([BillTextWords.KeyTermsLabel], await LabelsAsync(database));

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            Assert.Equal([Quote], await ViewsSeed.QueryAsPublicAsync(database, "SELECT quote FROM public_api.bill_key_terms"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static Task<string[]> LabelsAsync(string database) => ViewsSeed.QueryAsPublicAsync(database, "SELECT label FROM public_api.bill_key_terms");

    private static async Task MigrateAsync(string database, string target)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }
}
