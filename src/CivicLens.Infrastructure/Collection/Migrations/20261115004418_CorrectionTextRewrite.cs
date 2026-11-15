using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Replaces <c>facts_unchanged</c> with <c>text_rewrite</c> in the correction history and the withdrawal corrections, and in their public views. Only a
/// vote entry could set <c>facts_unchanged</c>, and every vote text rewrite before this change was model text. So true becomes Model and false becomes
/// None. The rollback refuses a Code entry, because the bool cannot hold it.
/// </summary>
public partial class CorrectionTextRewrite : Migration
{
    private const string DropViewsSql = """
        DROP VIEW public_api.record_correction_history_entries;
        DROP VIEW public_api.withdrawal_corrections;
        """;

    private const string UpSql = """
        ALTER TABLE published_correction_history_entries DROP CONSTRAINT ck_published_correction_history_entries_note_facts_unchanged;
        ALTER TABLE published_correction_history_entries ADD COLUMN text_rewrite character varying(16);
        UPDATE published_correction_history_entries SET text_rewrite = CASE WHEN facts_unchanged THEN 'Model' ELSE 'None' END;
        ALTER TABLE published_correction_history_entries ALTER COLUMN text_rewrite SET NOT NULL;
        ALTER TABLE published_correction_history_entries DROP COLUMN facts_unchanged;
        ALTER TABLE published_correction_history_entries
            ADD CONSTRAINT ck_published_correction_history_entries_text_rewrite CHECK (text_rewrite IN ('None', 'Model', 'Code'));
        ALTER TABLE published_correction_history_entries
            ADD CONSTRAINT ck_published_correction_history_entries_note_text_rewrite CHECK (note IS NULL OR text_rewrite = 'None');

        ALTER TABLE publication_withdrawal_corrections DROP CONSTRAINT ck_publication_withdrawal_corrections_facts_unchanged;
        ALTER TABLE publication_withdrawal_corrections ADD COLUMN text_rewrite character varying(16);
        UPDATE publication_withdrawal_corrections SET text_rewrite = CASE WHEN facts_unchanged THEN 'Model' ELSE 'None' END;
        ALTER TABLE publication_withdrawal_corrections ALTER COLUMN text_rewrite SET NOT NULL;
        ALTER TABLE publication_withdrawal_corrections DROP COLUMN facts_unchanged;
        ALTER TABLE publication_withdrawal_corrections
            ADD CONSTRAINT ck_publication_withdrawal_corrections_text_rewrite CHECK (text_rewrite IN ('None', 'Model', 'Code'));
        ALTER TABLE publication_withdrawal_corrections
            ADD CONSTRAINT ck_publication_withdrawal_corrections_note_text_rewrite CHECK (note IS NULL OR text_rewrite = 'None');
        """;

    private const string DownSql = """
        DO $$
        BEGIN
          IF EXISTS (SELECT 1 FROM published_correction_history_entries WHERE text_rewrite = 'Code')
             OR EXISTS (SELECT 1 FROM publication_withdrawal_corrections WHERE text_rewrite = 'Code') THEN
            RAISE EXCEPTION 'A correction entry names a code rewrite, and facts_unchanged cannot hold it.';
          END IF;
        END $$;

        ALTER TABLE published_correction_history_entries DROP CONSTRAINT ck_published_correction_history_entries_note_text_rewrite;
        ALTER TABLE published_correction_history_entries DROP CONSTRAINT ck_published_correction_history_entries_text_rewrite;
        ALTER TABLE published_correction_history_entries ADD COLUMN facts_unchanged boolean;
        UPDATE published_correction_history_entries SET facts_unchanged = text_rewrite = 'Model';
        ALTER TABLE published_correction_history_entries ALTER COLUMN facts_unchanged SET NOT NULL;
        ALTER TABLE published_correction_history_entries DROP COLUMN text_rewrite;
        ALTER TABLE published_correction_history_entries
            ADD CONSTRAINT ck_published_correction_history_entries_note_facts_unchanged CHECK (note IS NULL OR NOT facts_unchanged);

        ALTER TABLE publication_withdrawal_corrections DROP CONSTRAINT ck_publication_withdrawal_corrections_note_text_rewrite;
        ALTER TABLE publication_withdrawal_corrections DROP CONSTRAINT ck_publication_withdrawal_corrections_text_rewrite;
        ALTER TABLE publication_withdrawal_corrections ADD COLUMN facts_unchanged boolean;
        UPDATE publication_withdrawal_corrections SET facts_unchanged = text_rewrite = 'Model';
        ALTER TABLE publication_withdrawal_corrections ALTER COLUMN facts_unchanged SET NOT NULL;
        ALTER TABLE publication_withdrawal_corrections DROP COLUMN text_rewrite;
        ALTER TABLE publication_withdrawal_corrections
            ADD CONSTRAINT ck_publication_withdrawal_corrections_facts_unchanged CHECK (note IS NULL OR NOT facts_unchanged);
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DropViewsSql);
        migrationBuilder.Sql(UpSql);
        migrationBuilder.Sql(ViewsSql("text_rewrite"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DropViewsSql);
        migrationBuilder.Sql(DownSql);
        migrationBuilder.Sql(ViewsSql("facts_unchanged"));
    }

    private static string ViewsSql(string column) => $"""
        GRANT SELECT (history_id, "position", replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, {column})
            ON published_correction_history_entries TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, {column})
            ON publication_withdrawal_corrections TO civic_lens_public_owner;

        CREATE VIEW public_api.record_correction_history_entries WITH (security_barrier = true) AS
        SELECT h.release_number, h.kind, h.record_id, h.revision_number, e."position",
               e.replaced_revision_number, e.published_at_utc_ticks, e.published_offset_minutes,
               e.note, e.{column}
          FROM public_api.record_correction_histories h
          JOIN public.published_correction_history_entries e ON e.history_id = h.history_id;

        CREATE VIEW public_api.withdrawal_corrections WITH (security_barrier = true) AS
        SELECT r.release_number, c.kind, c.record_id, c.first_release, c."position",
               c.replaced_revision_number, c.published_at_utc_ticks, c.note, c.{column}
          FROM public.publication_releases r
          JOIN public.publication_withdrawals w
            ON w.first_release <= r.release_number AND (w.last_release IS NULL OR w.last_release >= r.release_number)
          JOIN public.publication_withdrawal_corrections c
            ON c.kind = w.kind AND c.record_id = w.record_id AND c.first_release = w.first_release
         WHERE r.is_active;

        ALTER VIEW public_api.record_correction_history_entries OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.withdrawal_corrections OWNER TO civic_lens_public_owner;
        """;
}
