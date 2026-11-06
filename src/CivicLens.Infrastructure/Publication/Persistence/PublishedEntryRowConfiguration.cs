using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedEntryRowConfiguration : IEntityTypeConfiguration<PublishedEntryRow>
{
    private const string RecordIdPattern = "'^[0-9a-f]{64}$'";

    public void Configure(EntityTypeBuilder<PublishedEntryRow> builder)
    {
        builder.ToTable("published_entries", table =>
        {
            table.HasCheckConstraint("ck_published_entries_kind", EnumChecks.InList("kind", KindNames));
            table.HasCheckConstraint("ck_published_entries_record_id",
                $"record_id ~ CASE WHEN kind = '{Name(PublishedRecordKind.DocumentChange)}' THEN '^[0-9a-f]{{32}}$' ELSE {RecordIdPattern} END");
            table.HasCheckConstraint("ck_published_entries_revision_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_published_entries_revision_key", RevisionKeyCheck());
            table.HasCheckConstraint("ck_published_entries_headline", "length(btrim(headline)) > 0");
            table.HasCheckConstraint("ck_published_entries_source_host", "source_host IS NULL OR length(btrim(source_host)) > 0");
            table.HasCheckConstraint("ck_published_entries_bill_title",
                $"(kind = '{Name(PublishedRecordKind.Action)}') = (bill_title IS NOT NULL) AND (bill_title IS NULL OR length(btrim(bill_title)) > 0)");
            table.HasCheckConstraint("ck_published_entries_approved_by",
                "approved_by IS NULL OR (kind = 'statement' AND approved_by IN ('Code', 'Person'))");
            table.HasCheckConstraint("ck_published_entries_bill_line",
                $"bill_line IS NULL OR (kind = '{Name(PublishedRecordKind.Action)}' AND length(btrim(bill_line)) >= 1 AND " +
                $"length(bill_line) <= {PublishedRecordEntry.MaximumBillLineLength})");
            table.HasCheckConstraint("ck_published_entries_document_type",
                $"(kind = '{Name(PublishedRecordKind.ExecutiveAction)}') = (document_type IS NOT NULL) AND " +
                $"(document_type IS NULL OR {EnumChecks.InList("document_type", Enum.GetNames<PublishedExecutiveActionKind>())})");
            table.HasCheckConstraint("ck_published_entries_link",
                $"(kind = '{Name(PublishedRecordKind.Relationship)}') = (link_type IS NOT NULL) AND " +
                "num_nonnulls(link_type, statement_record_id, target_kind, target_record_id) IN (0, 4) AND " +
                $"(link_type IS NULL OR ({EnumChecks.InList("link_type", Enum.GetNames<PublishedRelationshipType>())} AND " +
                $"{EnumChecks.InList("target_kind", [Name(PublishedRecordKind.Action), Name(PublishedRecordKind.Vote)])} AND " +
                $"statement_record_id ~ {RecordIdPattern} AND target_record_id ~ {RecordIdPattern} AND " +
                $"(target_kind = '{Name(PublishedRecordKind.Vote)}') = (link_type = '{PublishedRelationshipType.StatementAboutVote}')))");
        });
        builder.HasKey(row => new { row.Kind, row.RecordId, row.RevisionNumber });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Headline).HasColumnName("headline").IsRequired();
        builder.Property(row => row.SourceHost).HasColumnName("source_host").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.EventDate).HasColumnName("event_date");
        builder.Property(row => row.BillTitle).HasColumnName("bill_title").HasMaxLength(PublishedAction.MaximumBillTitleLength);
        builder.Property(row => row.LinkType).HasColumnName("link_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(row => row.StatementRecordId).HasColumnName("statement_record_id").HasMaxLength(64);
        builder.Property(row => row.StatementKind).HasColumnName("statement_kind").HasMaxLength(32)
            .HasComputedColumnSql($"CASE WHEN statement_record_id IS NULL THEN NULL ELSE '{Name(PublishedRecordKind.Statement)}' END", stored: true);
        builder.Property(row => row.TargetKind).HasColumnName("target_kind").HasMaxLength(32);
        builder.Property(row => row.TargetRecordId).HasColumnName("target_record_id").HasMaxLength(64);
        builder.Property(row => row.DocumentType).HasColumnName("document_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(row => row.BillLine).HasColumnName("bill_line");
        builder.Property(row => row.ApprovedBy).HasColumnName("approved_by").HasConversion<string>().HasMaxLength(32);

        ConfigureRevisionColumns(builder);
        builder.HasOne<PublishedRecordRow>().WithMany().HasForeignKey(row => new { row.Kind, row.RecordId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedRecordRow>().WithMany().HasForeignKey(row => new { row.StatementKind, row.StatementRecordId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedRecordRow>().WithMany().HasForeignKey(row => new { row.TargetKind, row.TargetRecordId })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRevisionColumns(EntityTypeBuilder<PublishedEntryRow> builder)
    {
        builder.Property(row => row.ActionRecordId).HasColumnName("action_record_id").HasMaxLength(64);
        builder.Property(row => row.ActionRevisionNumber).HasColumnName("action_revision_number");
        builder.HasOne<ActionRevisionRow>().WithMany().HasForeignKey(row => new { row.ActionRecordId, row.ActionRevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(row => row.BriefRecordId).HasColumnName("brief_record_id").HasMaxLength(64);
        builder.Property(row => row.BriefRevisionNumber).HasColumnName("brief_revision_number");
        builder.HasOne<BriefRevisionRow>().WithMany().HasForeignKey(row => new { row.BriefRecordId, row.BriefRevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(row => row.ExecutiveActionRecordId).HasColumnName("executive_action_record_id").HasMaxLength(64);
        builder.Property(row => row.ExecutiveActionRevisionNumber).HasColumnName("executive_action_revision_number");
        builder.HasOne<ExecutiveActionRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.ExecutiveActionRecordId, row.ExecutiveActionRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(row => row.RelationshipRecordId).HasColumnName("relationship_record_id").HasMaxLength(64);
        builder.Property(row => row.RelationshipRevisionNumber).HasColumnName("relationship_revision_number");
        builder.HasOne<RelationshipRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.RelationshipRecordId, row.RelationshipRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(row => row.StatementCandidateId).HasColumnName("statement_candidate_id").HasMaxLength(64);
        builder.Property(row => row.StatementRevisionNumber).HasColumnName("statement_revision_number");
        builder.HasOne<StatementRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.StatementCandidateId, row.StatementRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(row => row.DocumentChangeDraftId).HasColumnName("document_change_draft_id").HasMaxLength(32);
        builder.Property(row => row.DocumentChangeRevisionNumber).HasColumnName("document_change_revision_number");
        builder.HasOne<DocumentChangeRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.DocumentChangeDraftId, row.DocumentChangeRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(row => row.VoteRecordId).HasColumnName("vote_record_id").HasMaxLength(64);
        builder.Property(row => row.VoteRevisionNumber).HasColumnName("vote_revision_number");
        builder.HasOne<VoteRevisionRow>().WithMany().HasForeignKey(row => new { row.VoteRecordId, row.VoteRevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string RevisionKeyCheck() => string.Join(" OR ", Enum.GetValues<PublishedRecordKind>().Select(kind =>
    {
        var (id, number) = PublishedEntryRevisionColumns.For(kind);
        var others = Enum.GetValues<PublishedRecordKind>().Where(other => other != kind).SelectMany(other =>
        {
            var (otherId, otherNumber) = PublishedEntryRevisionColumns.For(other);
            return new[] { otherId, otherNumber };
        });
        return $"(kind = '{Name(kind)}' AND {id} IS NOT DISTINCT FROM record_id AND {number} IS NOT DISTINCT FROM revision_number AND " +
            $"num_nonnulls({string.Join(", ", others)}) = 0)";
    }));

    private static IEnumerable<string> KindNames => Enum.GetValues<PublishedRecordKind>().Select(Name);

    private static string Name(PublishedRecordKind kind) => PublishedRecordReference.NameOf(kind);
}
