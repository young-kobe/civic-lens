using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiCorrectionHistories : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.record_correction_histories WITH (security_barrier = true) AS
        SELECT r.release_number, c.kind, c.record_id, c.revision_number,
               h.history_id, h.encoding_version, h.correction_count
          FROM public.publication_releases r
          JOIN public.release_correction_histories c ON c.release_number = r.release_number
          JOIN public.publication_record_states s
            ON s.kind = c.kind AND s.record_id = c.record_id AND s.revision_number = c.revision_number
           AND s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)
          JOIN public.published_correction_histories h ON h.history_id = c.history_id
         WHERE r.is_active;

        CREATE VIEW public_api.record_correction_history_entries WITH (security_barrier = true) AS
        SELECT h.release_number, h.kind, h.record_id, h.revision_number, e."position",
               e.replaced_revision_number, e.published_at_utc_ticks, e.published_offset_minutes,
               e.note, e.facts_unchanged
          FROM public_api.record_correction_histories h
          JOIN public.published_correction_history_entries e ON e.history_id = h.history_id;

        ALTER VIEW public_api.record_correction_histories OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.record_correction_history_entries OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = """
        DROP VIEW public_api.record_correction_history_entries;
        DROP VIEW public_api.record_correction_histories;
        REVOKE SELECT (release_number, kind, record_id, revision_number, history_id) ON release_correction_histories FROM civic_lens_public_owner;
        REVOKE SELECT (history_id, encoding_version, correction_count) ON published_correction_histories FROM civic_lens_public_owner;
        REVOKE SELECT (history_id, "position", replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, facts_unchanged)
            ON published_correction_history_entries FROM civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
