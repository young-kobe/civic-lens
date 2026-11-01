using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DropDecisionJson : Migration
    {
        private const long DotNetEpochMicroseconds = 62_135_596_800_000_000;

        private static readonly (string Prefix, string IdColumn)[] DecisionTables =
        [
            ("document_change", "draft_id"),
            ("statement", "candidate_id"),
            ("action", "record_id"),
            ("vote", "record_id"),
            ("executive_action", "record_id"),
            ("relationship", "record_id"),
            ("brief", "record_id")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (prefix, idColumn) in DecisionTables)
            {
                migrationBuilder.Sql($"LOCK TABLE {prefix}_review_decisions IN SHARE ROW EXCLUSIVE MODE");
                migrationBuilder.Sql(CheckSql(prefix, idColumn));
                migrationBuilder.DropColumn(name: "decision_json", table: $"{prefix}_review_decisions");
            }
        }

        private static string CheckSql(string prefix, string idColumn)
        {
            var decisions = $"{prefix}_review_decisions";
            var resolutions = $"{prefix}_review_decision_resolutions";
            return $$"""
                DO $check$
                DECLARE mismatch text;
                BEGIN
                    SELECT d.decision_id INTO mismatch FROM {{decisions}} d
                    WHERE d.decision_id IS DISTINCT FROM d.decision_json::jsonb ->> 'decisionId'
                        OR d.{{idColumn}} IS DISTINCT FROM d.decision_json::jsonb ->> 'draftId'
                        OR d.revision_number IS DISTINCT FROM (d.decision_json::jsonb ->> 'revisionNumber')::int
                        OR d.review_state_version IS DISTINCT FROM (d.decision_json::jsonb ->> 'reviewStateVersion')::int
                        OR d.kind IS DISTINCT FROM CASE (d.decision_json::jsonb ->> 'kind')
                            WHEN '0' THEN 'Approve' WHEN '1' THEN 'RequestChanges' WHEN '2' THEN 'WithdrawApproval' END
                        OR d.actor_subject IS DISTINCT FROM d.decision_json::jsonb ->> 'actorSubject'
                        OR d.note IS DISTINCT FROM d.decision_json::jsonb ->> 'note'
                        OR d.correction_note IS DISTINCT FROM d.decision_json::jsonb ->> 'correctionNote'
                        OR coalesce(abs(d.created_at_utc_ticks / 10 - {{DotNetEpochMicroseconds}}
                            - round(extract(epoch FROM (d.decision_json::jsonb ->> 'createdAtUtc')::timestamptz)::numeric * 1000000)) > 1, true)
                        OR (SELECT coalesce(array_agg(r.resolved_decision_id::text ORDER BY r.resolved_decision_id), '{}')
                            FROM {{resolutions}} r WHERE r.decision_id = d.decision_id)
                            IS DISTINCT FROM (SELECT coalesce(array_agg(j.value ORDER BY j.value), '{}')
                            FROM jsonb_array_elements_text(d.decision_json::jsonb -> 'resolvedDecisionIds') AS j(value))
                    ORDER BY d.decision_id LIMIT 1;
                    IF mismatch IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot drop decision_json: {{decisions}} differs from its columns for decision %', mismatch;
                    END IF;
                END
                $check$
                """;
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (prefix, idColumn) in DecisionTables)
                migrationBuilder.Sql($$"""
                    LOCK TABLE {{prefix}}_review_decisions IN SHARE ROW EXCLUSIVE MODE;
                    ALTER TABLE {{prefix}}_review_decisions ADD COLUMN decision_json text;
                    UPDATE {{prefix}}_review_decisions d SET decision_json = jsonb_build_object(
                        'decisionId', d.decision_id,
                        'draftId', d.{{idColumn}},
                        'revisionNumber', d.revision_number,
                        'reviewStateVersion', d.review_state_version,
                        'kind', CASE d.kind WHEN 'Approve' THEN 0 WHEN 'RequestChanges' THEN 1 ELSE 2 END,
                        'actorSubject', d.actor_subject,
                        'note', d.note,
                        'resolvedDecisionIds', COALESCE((SELECT jsonb_agg(r.resolved_decision_id ORDER BY r.resolved_decision_id)
                            FROM {{prefix}}_review_decision_resolutions r WHERE r.decision_id = d.decision_id), '[]'::jsonb),
                        'createdAtUtc', to_char(timestamp '0001-01-01' + (d.created_at_utc_ticks / 864000000000) * interval '1 day'
                            + (d.created_at_utc_ticks % 864000000000 / 10) * interval '1 microsecond', 'YYYY-MM-DD"T"HH24:MI:SS') || '.' || lpad((d.created_at_utc_ticks % 10000000)::text, 7, '0') || '+00:00',
                        'correctionNote', d.correction_note)::text;
                    ALTER TABLE {{prefix}}_review_decisions ALTER COLUMN decision_json SET NOT NULL;
                    """);
        }
    }
}
