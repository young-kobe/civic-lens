using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012210000_AnalysisWakeupPayload")]
public partial class AnalysisWakeupPayload : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION civic_lens_notify_analysis_change() RETURNS trigger AS $$
            BEGIN
                PERFORM pg_notify('civic_lens_analysis', TG_TABLE_NAME || ':' || NEW.task);
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION civic_lens_notify_analysis_change() RETURNS trigger AS $$
            BEGIN
                PERFORM pg_notify('civic_lens_analysis', 'changed');
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
    }
}
