using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261105120000_ReviewListColumns")]
public partial class ReviewListColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_action_drafts_needs_action_revised;");
        migrationBuilder.DropColumn(name: "last_revised_at_utc_ticks", table: "action_drafts");
        migrationBuilder.DropColumn(name: "last_revised_at_utc_ticks", table: "vote_drafts");
        migrationBuilder.AddColumn<string>(name: "candidate_id", table: "action_drafts", type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<DateOnly>(name: "event_on", table: "action_drafts", type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
        migrationBuilder.AddColumn<string[]>(name: "issue_ids", table: "action_drafts", type: "text[]", nullable: false, defaultValueSql: "'{}'");
        migrationBuilder.AddColumn<string[]>(name: "official_ids", table: "action_drafts", type: "text[]", nullable: false, defaultValueSql: "'{}'");
        migrationBuilder.CreateIndex(name: "ix_action_drafts_issue_ids", table: "action_drafts", column: "issue_ids").Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.CreateIndex(name: "ix_action_drafts_official_ids", table: "action_drafts", column: "official_ids").Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.AddColumn<string>(name: "candidate_id", table: "vote_drafts", type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<DateOnly>(name: "event_on", table: "vote_drafts", type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
        migrationBuilder.AddColumn<string[]>(name: "issue_ids", table: "vote_drafts", type: "text[]", nullable: false, defaultValueSql: "'{}'");
        migrationBuilder.AddColumn<string[]>(name: "official_ids", table: "vote_drafts", type: "text[]", nullable: false, defaultValueSql: "'{}'");
        migrationBuilder.CreateIndex(name: "ix_vote_drafts_issue_ids", table: "vote_drafts", column: "issue_ids").Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.CreateIndex(name: "ix_vote_drafts_official_ids", table: "vote_drafts", column: "official_ids").Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.AddColumn<DateOnly>(name: "event_on", table: "statement_drafts", type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
        migrationBuilder.AddColumn<string[]>(name: "issue_ids", table: "statement_drafts", type: "text[]", nullable: false, defaultValueSql: "'{}'");
        migrationBuilder.AddColumn<string[]>(name: "official_ids", table: "statement_drafts", type: "text[]", nullable: false, defaultValueSql: "'{}'");
        migrationBuilder.CreateIndex(name: "ix_statement_drafts_issue_ids", table: "statement_drafts", column: "issue_ids").Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.CreateIndex(name: "ix_statement_drafts_official_ids", table: "statement_drafts", column: "official_ids").Annotation("Npgsql:IndexMethod", "gin");
        migrationBuilder.CreateIndex(name: "ix_statement_drafts_listing", table: "statement_drafts", columns: new[] { "event_on", "candidate_id" }, descending: new[] { true, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "last_revised_at_utc_ticks", table: "action_drafts", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>(name: "last_revised_at_utc_ticks", table: "vote_drafts", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.Sql("""
            CREATE INDEX ix_action_drafts_needs_action_revised
                ON action_drafts (last_revised_at_utc_ticks DESC, record_id DESC) WHERE review_status <> 'Approved';
            """);
        migrationBuilder.DropIndex(name: "ix_action_drafts_official_ids", table: "action_drafts");
        migrationBuilder.DropIndex(name: "ix_action_drafts_issue_ids", table: "action_drafts");
        migrationBuilder.DropColumn(name: "official_ids", table: "action_drafts");
        migrationBuilder.DropColumn(name: "issue_ids", table: "action_drafts");
        migrationBuilder.DropColumn(name: "event_on", table: "action_drafts");
        migrationBuilder.DropColumn(name: "candidate_id", table: "action_drafts");
        migrationBuilder.DropIndex(name: "ix_vote_drafts_official_ids", table: "vote_drafts");
        migrationBuilder.DropIndex(name: "ix_vote_drafts_issue_ids", table: "vote_drafts");
        migrationBuilder.DropColumn(name: "official_ids", table: "vote_drafts");
        migrationBuilder.DropColumn(name: "issue_ids", table: "vote_drafts");
        migrationBuilder.DropColumn(name: "event_on", table: "vote_drafts");
        migrationBuilder.DropColumn(name: "candidate_id", table: "vote_drafts");
        migrationBuilder.DropIndex(name: "ix_statement_drafts_listing", table: "statement_drafts");
        migrationBuilder.DropIndex(name: "ix_statement_drafts_official_ids", table: "statement_drafts");
        migrationBuilder.DropIndex(name: "ix_statement_drafts_issue_ids", table: "statement_drafts");
        migrationBuilder.DropColumn(name: "official_ids", table: "statement_drafts");
        migrationBuilder.DropColumn(name: "issue_ids", table: "statement_drafts");
        migrationBuilder.DropColumn(name: "event_on", table: "statement_drafts");
    }
}
