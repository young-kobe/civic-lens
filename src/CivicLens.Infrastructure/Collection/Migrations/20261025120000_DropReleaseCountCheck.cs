using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261025120000_DropReleaseCountCheck")]
public partial class DropReleaseCountCheck : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_publication_releases_count", table: "publication_releases");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(name: "ck_publication_releases_count", table: "publication_releases",
            sql: "record_count BETWEEN 0 AND 2000");
    }
}
