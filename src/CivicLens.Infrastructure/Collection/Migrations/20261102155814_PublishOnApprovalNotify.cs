using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublishOnApprovalNotify : Migration
    {
        private const int GroupingSeconds = 60;
        private const int ReleaseSpacingSeconds = 600;

        private static readonly string[] DraftTables =
        [
            "action_drafts",
            "brief_drafts",
            "document_change_drafts",
            "executive_action_drafts",
            "relationship_drafts",
            "statement_drafts",
            "vote_drafts"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                CREATE FUNCTION civic_lens_notify_publishing_approval() RETURNS trigger AS $$
                DECLARE
                    now_ticks bigint := (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint;
                    last_release_ticks bigint := COALESCE((SELECT max(published_at_utc_ticks) FROM publication_releases), 0);
                BEGIN
                    PERFORM pg_notify('civic_lens_publishing',
                        GREATEST(now_ticks + {{GroupingSeconds}} * 10000000::bigint,
                            last_release_ticks + {{ReleaseSpacingSeconds}} * 10000000::bigint)::text);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            foreach (var table in DraftTables)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_{table}_publishing_insert_notify
                    AFTER INSERT ON {table}
                    FOR EACH ROW WHEN (NEW.review_status = 'Approved')
                    EXECUTE FUNCTION civic_lens_notify_publishing_approval();

                    CREATE TRIGGER trg_{table}_publishing_notify
                    AFTER UPDATE OF review_status, current_revision_number ON {table}
                    FOR EACH ROW WHEN (NEW.review_status = 'Approved' AND
                        (OLD.review_status IS DISTINCT FROM NEW.review_status OR OLD.current_revision_number IS DISTINCT FROM NEW.current_revision_number))
                    EXECUTE FUNCTION civic_lens_notify_publishing_approval();
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in DraftTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER trg_{table}_publishing_notify ON {table};");
                migrationBuilder.Sql($"DROP TRIGGER trg_{table}_publishing_insert_notify ON {table};");
            }
            migrationBuilder.Sql("DROP FUNCTION civic_lens_notify_publishing_approval();");
        }
    }
}
