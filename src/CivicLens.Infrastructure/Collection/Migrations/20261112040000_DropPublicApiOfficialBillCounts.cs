using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Drops the live bill count view. Every public reader reads the stored release bill counts, so no reader uses it. The rollback restores the view from <c>PublicApiOfficials</c>.</summary>
public partial class DropPublicApiOfficialBillCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP VIEW public_api.official_bill_counts;");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE VIEW public_api.official_bill_counts WITH (security_barrier = true) AS
        SELECT p.person_id AS official_id, r.congress,
               count(DISTINCT p.record_id) FILTER (WHERE p.role = 'Sponsor') AS sponsored,
               count(DISTINCT p.record_id) FILTER (WHERE p.role = 'Cosponsor' AND p.withdrawn_on IS NULL AND NOT EXISTS (
                   SELECT 1 FROM public.action_revision_sponsorships s
                    WHERE s.record_id = p.record_id AND s.revision_number = p.revision_number AND s.person_id = p.person_id AND s.role = 'Sponsor')) AS cosponsored
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
         GROUP BY p.person_id, r.congress;
        ALTER VIEW public_api.official_bill_counts OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.official_bill_counts TO civic_lens_public;
        """);
}
