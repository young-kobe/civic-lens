using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipRevisionRowConfiguration : IEntityTypeConfiguration<RelationshipRevisionRow>
{
    private const string Hex64 = "'^[0-9a-f]{64}$'";
    private const string ActionTarget = "target_action_record_id, target_action_revision_number";
    private const string VoteTarget = "target_vote_record_id, target_vote_revision_number";

    private static readonly string[] TargetKinds = [nameof(RelationshipRecordKind.Action), nameof(RelationshipRecordKind.Vote)];

    private static readonly string[] WrittenTogether =
    [
        "statement_candidate_id", "statement_revision_number", "target_kind", "match_basis", "match_extraction_id", "match_start", "match_length", "type",
        "reason", "statement_quote_extraction_id", "statement_quote_start", "statement_quote_length", "target_fact_id", "official_id"
    ];

    public void Configure(EntityTypeBuilder<RelationshipRevisionRow> builder)
    {
        builder.ToTable("relationship_revisions", table =>
        {
            table.HasCheckConstraint("ck_relationship_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_relationship_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_relationship_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_relationship_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
            table.HasCheckConstraint("ck_relationship_revisions_statement", $"statement_candidate_id ~ {Hex64} AND statement_revision_number >= 1");
            table.HasCheckConstraint("ck_relationship_revisions_target", $"""
                target_kind IS NULL AND num_nonnulls({ActionTarget}, {VoteTarget}) = 0
                OR target_kind = '{nameof(RelationshipRecordKind.Action)}' AND num_nonnulls({ActionTarget}) = 2 AND num_nonnulls({VoteTarget}) = 0
                OR target_kind = '{nameof(RelationshipRecordKind.Vote)}' AND num_nonnulls({VoteTarget}) = 2 AND num_nonnulls({ActionTarget}) = 0
                """);
            table.HasCheckConstraint("ck_relationship_revisions_target_ids",
                $"target_action_record_id ~ {Hex64} AND target_action_revision_number >= 1 AND target_vote_record_id ~ {Hex64} AND target_vote_revision_number >= 1");
            table.HasCheckConstraint("ck_relationship_revisions_match_basis", EnumChecks.InList("match_basis", Enum.GetNames<RelationshipMatchBasis>()));
            table.HasCheckConstraint("ck_relationship_revisions_type", EnumChecks.InList("type", Enum.GetNames<RelationshipType>()));
            table.HasCheckConstraint("ck_relationship_revisions_type_target", TypeFitsTarget());
            table.HasCheckConstraint("ck_relationship_revisions_reason",
                $"btrim(reason) <> '' AND length(reason) <= {RelationshipRevision.MaximumReasonLength} AND reason !~ '[\\r\\n]'");
            table.HasCheckConstraint("ck_relationship_revisions_fact_id",
                $"btrim(target_fact_id) <> '' AND length(target_fact_id) <= {RelationshipRevision.MaximumFactIdLength}");
            table.HasCheckConstraint("ck_relationship_revisions_official_id",
                $"btrim(official_id) <> '' AND length(official_id) <= {RelationshipRevision.MaximumOfficialIdLength}");
            table.HasCheckConstraint("ck_relationship_revisions_quote", $"""
                statement_quote_length <= {StatementDraftRevision.MaximumQuotationLength} AND statement_quote_extraction_id = match_extraction_id
                AND statement_quote_start <= match_start AND match_start + match_length <= statement_quote_start + statement_quote_length
                """);
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.StatementCandidateId).HasColumnName("statement_candidate_id").HasMaxLength(64);
        builder.Property(row => row.StatementRevisionNumber).HasColumnName("statement_revision_number");
        builder.Property(row => row.TargetKind).HasColumnName("target_kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.TargetActionRecordId).HasColumnName("target_action_record_id").HasMaxLength(64);
        builder.Property(row => row.TargetActionRevisionNumber).HasColumnName("target_action_revision_number");
        builder.Property(row => row.TargetVoteRecordId).HasColumnName("target_vote_record_id").HasMaxLength(64);
        builder.Property(row => row.TargetVoteRevisionNumber).HasColumnName("target_vote_revision_number");
        builder.Property(row => row.MatchBasis).HasColumnName("match_basis").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(24);
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(RelationshipRevision.MaximumReasonLength);
        builder.Property(row => row.TargetFactId).HasColumnName("target_fact_id").HasMaxLength(RelationshipRevision.MaximumFactIdLength);
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(RelationshipRevision.MaximumOfficialIdLength);
        SourceSpanColumns.Configure(builder, "relationship_revisions", "Match", "match", optional: false);
        SourceSpanColumns.Configure(builder, "relationship_revisions", "StatementQuote", "statement_quote", optional: false);
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<RelationshipDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RelationshipAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StatementRevisionRow>().WithMany().HasForeignKey(row => new { row.StatementCandidateId, row.StatementRevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionRevisionRow>().WithMany().HasForeignKey(row => new { row.TargetActionRecordId, row.TargetActionRevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VoteRevisionRow>().WithMany().HasForeignKey(row => new { row.TargetVoteRecordId, row.TargetVoteRevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string TypeFitsTarget() => string.Join(" OR ", TargetKinds.Select(kind =>
        $"target_kind = '{kind}' AND {EnumChecks.InList("type", RelationshipTypes.For(Enum.Parse<RelationshipRecordKind>(kind)).Select(type => type.ToString()))}"));
}
