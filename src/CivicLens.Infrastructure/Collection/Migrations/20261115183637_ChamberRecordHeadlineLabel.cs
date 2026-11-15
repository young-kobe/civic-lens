using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// <c>ChamberVoteCodeLabel</c> and <c>ShortStatementSourceText</c> share a timestamp, so the second replaced <c>public_api.record_headlines</c> with the
/// Congress.gov label for every code vote. This view keeps the statement label of <c>ShortStatementSourceText</c> and gives the vote code label the
/// chamber rule again. The rollback restores the view of <c>ShortStatementSourceText</c>.
/// </summary>
public partial class ChamberRecordHeadlineLabel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ShortStatementSourceText.RecordHeadlinesView(ChamberVoteCodeLabel.VoteCodeLabel("r", byChamber: true)));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ShortStatementSourceText.RecordHeadlinesView(ChamberVoteCodeLabel.VoteCodeLabel("r", byChamber: false)));
}
