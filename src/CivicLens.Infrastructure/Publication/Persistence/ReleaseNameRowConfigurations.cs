using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialRowConfiguration : IEntityTypeConfiguration<ReleaseOfficialRow>
{
    public void Configure(EntityTypeBuilder<ReleaseOfficialRow> builder)
    {
        builder.ToTable("release_officials", table =>
        {
            table.HasCheckConstraint("ck_release_officials_official_id", "length(btrim(official_id)) > 0");
            table.HasCheckConstraint("ck_release_officials_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.OfficialId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.WikidataId).HasColumnName("wikidata_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Website).HasColumnName("website");
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReleaseOfficialTermRowConfiguration : IEntityTypeConfiguration<ReleaseOfficialTermRow>
{
    public void Configure(EntityTypeBuilder<ReleaseOfficialTermRow> builder)
    {
        builder.ToTable("release_official_terms", table =>
        {
            table.HasCheckConstraint("ck_release_official_terms_position", "position >= 0");
            table.HasCheckConstraint("ck_release_official_terms_office", "length(btrim(office)) > 0");
            table.HasCheckConstraint("ck_release_official_terms_district", "district IS NULL OR district >= 0");
            table.HasCheckConstraint("ck_release_official_terms_dates", "end_on IS NULL OR end_on >= start_on");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.OfficialId, row.Position });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Office).HasColumnName("office").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        builder.Property(row => row.State).HasColumnName("state").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.District).HasColumnName("district");
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Caucus).HasColumnName("caucus").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.StartOn).HasColumnName("start_on");
        builder.Property(row => row.EndOn).HasColumnName("end_on");
        builder.HasOne<ReleaseOfficialRow>().WithMany().HasForeignKey(row => new { row.ReleaseNumber, row.OfficialId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReleaseIssueRowConfiguration : IEntityTypeConfiguration<ReleaseIssueRow>
{
    public void Configure(EntityTypeBuilder<ReleaseIssueRow> builder)
    {
        builder.ToTable("release_issues", table =>
        {
            table.HasCheckConstraint("ck_release_issues_issue_id", "length(btrim(issue_id)) > 0");
            table.HasCheckConstraint("ck_release_issues_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Definition).HasColumnName("definition");
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}

internal abstract class ReleaseIssueTermRowConfiguration<TRow>(string table) : IEntityTypeConfiguration<TRow> where TRow : ReleaseIssueTermRow
{
    public void Configure(EntityTypeBuilder<TRow> builder)
    {
        builder.ToTable(table, check =>
        {
            check.HasCheckConstraint($"ck_{table}_position", "position >= 0");
            check.HasCheckConstraint($"ck_{table}_text", "length(btrim(text)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId, row.Position });
        builder.HasAlternateKey(row => new { row.ReleaseNumber, row.IssueId, row.Text });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Text).HasColumnName("text");
        builder.HasOne<ReleaseIssueRow>().WithMany().HasForeignKey(row => new { row.ReleaseNumber, row.IssueId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReleaseIssueIncludeRowConfiguration() : ReleaseIssueTermRowConfiguration<ReleaseIssueIncludeRow>("release_issue_includes");

internal sealed class ReleaseIssueExcludeRowConfiguration() : ReleaseIssueTermRowConfiguration<ReleaseIssueExcludeRow>("release_issue_excludes");

internal sealed class ReleasePolicyAreaRowConfiguration : IEntityTypeConfiguration<ReleasePolicyAreaRow>
{
    public void Configure(EntityTypeBuilder<ReleasePolicyAreaRow> builder)
    {
        builder.ToTable("release_policy_areas", table =>
        {
            table.HasCheckConstraint("ck_release_policy_areas_policy_area_id", "length(btrim(policy_area_id)) > 0");
            table.HasCheckConstraint("ck_release_policy_areas_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.PolicyAreaId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.PolicyAreaId).HasColumnName("policy_area_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
