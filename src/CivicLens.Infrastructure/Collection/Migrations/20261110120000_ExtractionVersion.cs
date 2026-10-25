using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261110120000_ExtractionVersion")]
public partial class ExtractionVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_document_extractions_versions", "document_extractions");
        migrationBuilder.DropColumn(name: "normalization_version", table: "document_extractions");
        migrationBuilder.RenameColumn(name: "parser_version", table: "document_extractions", newName: "extraction_version");
        migrationBuilder.AddCheckConstraint("ck_document_extractions_versions", "document_extractions",
            "length(btrim(extraction_version)) > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_document_extractions_versions", "document_extractions");
        migrationBuilder.RenameColumn(name: "extraction_version", table: "document_extractions", newName: "parser_version");
        migrationBuilder.AddColumn<string>(name: "normalization_version", table: "document_extractions",
            type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: "unknown");
        migrationBuilder.Sql("ALTER TABLE document_extractions ALTER COLUMN normalization_version DROP DEFAULT");
        migrationBuilder.AddCheckConstraint("ck_document_extractions_versions", "document_extractions",
            "length(btrim(parser_version)) > 0 AND length(btrim(normalization_version)) > 0");
    }
}
