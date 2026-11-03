using CivicLens.Application.Collection;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

/// <summary>An action draft that the editor page can render: its cited text is saved and its candidate row exists.</summary>
internal static class ActionPageSeed
{
    private const string SponsorText = "Sponsor: Warren, Elizabeth\nCosponsor: Other, Person\n";

    public static async Task<ActionDraftRevision> SeedAsync(ReviewWorkspaceHost host, RelationshipWorldSeeder seeder, string type = "hr",
        int congress = 119, int number = 1234)
    {
        var extraction = await host.SaveExtractionAsync(SponsorText);
        var action = ActionRevisionFixtures.Model(1) with
        {
            BillType = type,
            Congress = congress,
            BillNumber = number,
            Sponsorships = [ActionRevisionFixtures.Sponsorship() with { Citations = [new(extraction.ExtractionId, 0, 26), new(extraction.ExtractionId, 27, 24)] }],
            TrackedIssueIds = []
        };
        var configuration = CollectionConfigurationRevision.Create(HouseVoteFixtures.Configuration());
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at) VALUES (@revision, @configuration, 1);
            INSERT INTO action_candidates (candidate_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id,
                configuration_revision_id, created_at_utc_ticks, includes_summaries)
            VALUES (@candidate, @source, @congress, @type, @number, 1, 'detail', @revision, 1, false)
            """, ("revision", configuration.Id), ("configuration", configuration.Json), ("candidate", action.CandidateId),
            ("source", action.SourceId), ("congress", congress), ("type", type), ("number", number));
        await seeder.ActionAsync(action, "AwaitingReview");
        return action;
    }
}
