using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class OfficialPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "official_photos",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    credit = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_photos", x => x.attempt_id);
                    table.ForeignKey(
                        name: "FK_official_photos_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_official_photos_person_id",
                table: "official_photos",
                column: "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "official_photos");
        }
    }
}
