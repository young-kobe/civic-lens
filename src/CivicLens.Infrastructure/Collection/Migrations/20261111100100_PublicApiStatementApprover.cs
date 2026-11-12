using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Exposes who approved the published revision of each statement. The server-rendered statement page reads the approver here, so it never guesses it from the label.</summary>
public partial class PublicApiStatementApprover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE VIEW public_api.statement_approvers WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, e.approved_by
          FROM public_api.statements s
          JOIN public.published_entries e ON e.kind = 'statement' AND e.record_id = s.record_id AND e.revision_number = s.revision_number;
        ALTER VIEW public_api.statement_approvers OWNER TO civic_lens_public_owner;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP VIEW public_api.statement_approvers;");
}
