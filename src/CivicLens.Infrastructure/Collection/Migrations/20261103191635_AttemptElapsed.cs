using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AttemptElapsed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "elapsed_milliseconds",
                table: "collection_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_elapsed_milliseconds",
                table: "collection_attempts",
                sql: "elapsed_milliseconds IS NULL OR elapsed_milliseconds BETWEEN 0 AND 305000");

            migrationBuilder.Sql("""
                DO $$
                DECLARE cutoff bigint;
                BEGIN
                    SELECT COALESCE(max(observed_at_utc_ticks), 0) INTO cutoff FROM collection_attempts;
                    EXECUTE format('ALTER TABLE collection_attempts ADD CONSTRAINT ck_collection_attempts_elapsed_measured CHECK (elapsed_milliseconds IS NOT NULL OR observed_at_utc_ticks <= %s)', cutoff);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_elapsed_measured",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_elapsed_milliseconds",
                table: "collection_attempts");

            migrationBuilder.DropColumn(
                name: "elapsed_milliseconds",
                table: "collection_attempts");
        }
    }
}
