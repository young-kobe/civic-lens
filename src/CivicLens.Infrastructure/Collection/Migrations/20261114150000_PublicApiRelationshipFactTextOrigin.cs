using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the column <c>fact_text_origin</c> to <c>public_api.relationships</c>, so the cited fact label of the relationship page knows who wrote the fact.
/// The column comes last, so the view keeps every other column in place. The rollback drops the column, so it rebuilds the view from
/// <see cref="PublicApiRelationships"/> with its owner and its grant.
/// </summary>
public partial class PublicApiRelationshipFactTextOrigin : Migration
{
    private static readonly string UpSql = $"""
        CREATE OR REPLACE VIEW public_api.relationships WITH (security_barrier = true) AS
        {PublicApiRelationships.RelationshipsColumns},
               f.fact_text_origin
        {PublicApiRelationships.RelationshipsFrom};
        """;

    private static readonly string DownSql = $"""
        DROP VIEW public_api.relationships;

        CREATE VIEW public_api.relationships WITH (security_barrier = true) AS
        {PublicApiRelationships.RelationshipsColumns}
        {PublicApiRelationships.RelationshipsFrom};

        ALTER VIEW public_api.relationships OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.relationships TO civic_lens_public;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
