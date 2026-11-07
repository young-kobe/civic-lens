using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiWithdrawals : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.withdrawals WITH (security_barrier = true) AS
        SELECT r.release_number, w.kind, w.record_id, w.withdrawn_revision_number,
               p.first_published_at_utc_ticks, w.withdrawn_at_utc_ticks, w.note, w.first_release
          FROM public.publication_releases r
          JOIN public.publication_withdrawals w
            ON w.first_release <= r.release_number AND (w.last_release IS NULL OR w.last_release >= r.release_number)
          JOIN public.published_records p ON p.kind = w.kind AND p.record_id = w.record_id
         WHERE r.is_active;

        CREATE VIEW public_api.withdrawal_corrections WITH (security_barrier = true) AS
        SELECT r.release_number, c.kind, c.record_id, c.first_release, c."position",
               c.replaced_revision_number, c.published_at_utc_ticks, c.note, c.facts_unchanged
          FROM public.publication_releases r
          JOIN public.publication_withdrawals w
            ON w.first_release <= r.release_number AND (w.last_release IS NULL OR w.last_release >= r.release_number)
          JOIN public.publication_withdrawal_corrections c
            ON c.kind = w.kind AND c.record_id = w.record_id AND c.first_release = w.first_release
         WHERE r.is_active;

        ALTER VIEW public_api.withdrawals OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.withdrawal_corrections OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.withdrawals, public_api.withdrawal_corrections TO civic_lens_public;
        """;

    private const string DownSql = """
        REVOKE SELECT ON public_api.withdrawals, public_api.withdrawal_corrections FROM civic_lens_public;
        DROP VIEW public_api.withdrawal_corrections;
        DROP VIEW public_api.withdrawals;
        REVOKE SELECT (kind, record_id, first_release, last_release, withdrawn_revision_number, withdrawn_at_utc_ticks, note) ON publication_withdrawals FROM civic_lens_public_owner;
        REVOKE SELECT (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, facts_unchanged) ON publication_withdrawal_corrections FROM civic_lens_public_owner;
        REVOKE SELECT (kind, record_id, first_published_at_utc_ticks) ON published_records FROM civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
