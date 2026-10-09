using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261009120000_PublicationReleases")]
public partial class PublicationReleases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "publication_releases",
            columns: table => new
            {
                release_number = table.Column<int>(type: "integer", nullable: false),
                directory_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                record_count = table.Column<int>(type: "integer", nullable: false),
                records_json = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_publication_releases", x => x.release_number);
                table.CheckConstraint("ck_publication_releases_number", "release_number BETWEEN 1 AND 999999");
                table.CheckConstraint("ck_publication_releases_directory", "directory_name ~ '^[0-9]{6}-[0-9a-f]{32}$'");
                table.CheckConstraint("ck_publication_releases_published", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_publication_releases_count", "record_count BETWEEN 0 AND 256");
            });

        migrationBuilder.CreateIndex(
            name: "IX_publication_releases_directory_name",
            table: "publication_releases",
            column: "directory_name",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "publication_releases");
    }
}
