using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionRowConfiguration : IEntityTypeConfiguration<ExecutiveActionRevisionRow>
{
    private static readonly string[] WrittenTogether =
    [
        "source_id", "document_number", "kind", "title", "signed_on", "published_on", "federal_register_citation", "president_name", "person_id",
        "document_url"
    ];

    private static readonly string[] OneLineFields =
        ["document_number", "title", "federal_register_citation", "president_name", "document_url"];

    public void Configure(EntityTypeBuilder<ExecutiveActionRevisionRow> builder)
    {
        builder.ToTable("executive_action_revisions", table =>
        {
            table.HasCheckConstraint("ck_executive_action_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_executive_action_revisions_author",
                $"author_kind = '{nameof(ReviewAuthorKind.Code)}' AND author_subject = '{ReviewAuthor.CodeSubject}' OR " +
                $"author_kind = '{nameof(ReviewAuthorKind.Analysis)}' AND author_subject = '{ReviewAuthor.AnalysisSubject}'");
            table.HasCheckConstraint("ck_executive_action_revisions_run", $"(author_kind = '{nameof(ReviewAuthorKind.Analysis)}') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_executive_action_revisions_summary_written", "(summary_text_origin IS NULL) = (summary_cut IS NULL)");
            table.HasCheckConstraint("ck_executive_action_revisions_summary_analysis", $"author_kind <> '{nameof(ReviewAuthorKind.Analysis)}' OR summary_text_origin IS NOT NULL");
            table.HasCheckConstraint("ck_executive_action_revisions_summary_origin", EnumChecks.InList("summary_text_origin", [nameof(ReviewTextOrigin.Model)]));
            table.HasCheckConstraint("ck_executive_action_revisions_code_version", "code_version ~ '^[0-9a-f]{40}$'");
            table.HasCheckConstraint("ck_executive_action_revisions_kind", EnumChecks.InList("kind", Enum.GetNames<ExecutiveActionKind>()));
            table.HasCheckConstraint("ck_executive_action_revisions_ids",
                "length(btrim(source_id)) > 0 AND length(btrim(person_id)) > 0");
            table.HasCheckConstraint("ck_executive_action_revisions_fields",
                string.Join(" AND ", OneLineFields.Append("presidential_number").Select(OneLine)));
            table.HasCheckConstraint("ck_executive_action_revisions_dates", "signed_on <> '0001-01-01' AND published_on <> '0001-01-01'");
            table.HasCheckConstraint("ck_executive_action_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
            table.HasCheckConstraint("ck_executive_action_revisions_disposition_notes_written",
                "(disposition_notes IS NULL) = (disposition_notes_extraction_id IS NULL)");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(ReviewAuthor.MaximumSubjectLength).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.CodeVersion).HasColumnName("code_version").HasMaxLength(40);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.DocumentNumber).HasColumnName("document_number").HasMaxLength(ExecutiveActionRevision.MaximumFieldLength);
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.PresidentialNumber).HasColumnName("presidential_number").HasMaxLength(ExecutiveActionRevision.MaximumFieldLength);
        builder.Property(row => row.Title).HasColumnName("title").HasMaxLength(ExecutiveActionRevision.MaximumFieldLength);
        builder.Property(row => row.SignedOn).HasColumnName("signed_on").HasColumnType("date");
        builder.Property(row => row.PublishedOn).HasColumnName("published_on").HasColumnType("date");
        builder.Property(row => row.FederalRegisterCitation).HasColumnName("federal_register_citation").HasMaxLength(ExecutiveActionRevision.MaximumFieldLength);
        builder.Property(row => row.PresidentName).HasColumnName("president_name").HasMaxLength(ExecutiveActionRevision.MaximumFieldLength);
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.DocumentUrl).HasColumnName("document_url").HasMaxLength(ExecutiveActionRevision.MaximumFieldLength);
        builder.Property(row => row.DispositionNotes).HasColumnName("disposition_notes").HasMaxLength(ExecutiveActionRevision.MaximumDispositionNotesLength);
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.SummaryTextOrigin).HasColumnName("summary_text_origin").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.SummaryCut).HasColumnName("summary_cut");
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<ExecutiveSummaryAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.SummarySentences).WithOne().HasForeignKey(sentence => new { sentence.RecordId, sentence.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        SourceSpanColumns.Configure(builder, "executive_action_revisions", "DispositionNotes", "disposition_notes", optional: true);
        builder.HasOne<ExecutiveActionDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Facts).WithOne().HasForeignKey(fact => new { fact.RecordId, fact.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.Kind, row.PresidentialNumber });
        builder.HasMany(row => row.Notices).WithOne().HasForeignKey(notice => new { notice.RecordId, notice.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.DispositionTargets).WithOne().HasForeignKey(target => new { target.RecordId, target.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }

    private static string OneLine(string column) =>
        $"({column} IS NULL OR (btrim({column}) <> '' AND length({column}) <= {ExecutiveActionRevision.MaximumFieldLength} AND {column} !~ '[\\r\\n]'))";
}
