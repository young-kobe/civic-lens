using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal static class ReviewDecisionTableConfiguration
{
    private static readonly string KindCheck = "kind IN (" + string.Join(", ",
        Enum.GetNames<ReviewDecisionKind>().Select(name => $"'{name}'")) + ")";

    public static void ConfigureDecisionColumns<TRow>(EntityTypeBuilder<TRow> builder, string table, string recordIdProperty)
        where TRow : class, IReviewDecisionRow
    {
        builder.Property(row => row.ActorSubject).HasColumnName("actor_subject").HasMaxLength(ReviewAuthor.MaximumSubjectLength).IsRequired();
        builder.Property(row => row.Note).HasColumnName("note");
        builder.Property(row => row.CorrectionNote).HasColumnName("correction_note");
        builder.ToTable(table, tableBuilder => tableBuilder.HasCheckConstraint($"ck_{table}_kind", KindCheck));
        builder.HasAlternateKey(nameof(IReviewDecisionRow.DecisionId), recordIdProperty);
    }

    public static void ConfigureAllResolutions(ModelBuilder modelBuilder)
    {
        ConfigureResolutions<ReviewDecisionRow>(modelBuilder, "document_change_review_decisions", "draft_id", nameof(ReviewDecisionRow.DraftId), 32);
        ConfigureResolutions<StatementReviewDecisionRow>(modelBuilder, "statement_review_decisions", "candidate_id",
            nameof(StatementReviewDecisionRow.CandidateId), 64);
        ConfigureResolutions<ActionReviewDecisionRow>(modelBuilder, "action_review_decisions", "record_id", nameof(ActionReviewDecisionRow.RecordId), 64);
        ConfigureResolutions<VoteReviewDecisionRow>(modelBuilder, "vote_review_decisions", "record_id", nameof(VoteReviewDecisionRow.RecordId), 64);
        ConfigureResolutions<ExecutiveActionReviewDecisionRow>(modelBuilder, "executive_action_review_decisions", "record_id",
            nameof(ExecutiveActionReviewDecisionRow.RecordId), 64);
        ConfigureResolutions<RelationshipReviewDecisionRow>(modelBuilder, "relationship_review_decisions", "record_id",
            nameof(RelationshipReviewDecisionRow.RecordId), 64);
        ConfigureResolutions<BriefReviewDecisionRow>(modelBuilder, "brief_review_decisions", "record_id", nameof(BriefReviewDecisionRow.RecordId), 64);
    }

    private static void ConfigureResolutions<TDecisionRow>(ModelBuilder modelBuilder, string decisionTable, string recordIdColumn,
        string recordIdProperty, int recordIdLength) where TDecisionRow : class, IReviewDecisionRow
    {
        modelBuilder.Entity<ReviewDecisionResolutionRow<TDecisionRow>>(builder =>
        {
            builder.ToTable(decisionTable.Replace("_decisions", "_decision_resolutions", StringComparison.Ordinal));
            builder.HasKey(row => new { row.DecisionId, row.ResolvedDecisionId });
            builder.Property(row => row.DecisionId).HasColumnName("decision_id").HasMaxLength(32);
            builder.Property(row => row.RecordId).HasColumnName(recordIdColumn).HasMaxLength(recordIdLength).IsRequired();
            builder.Property(row => row.ResolvedDecisionId).HasColumnName("resolved_decision_id").HasMaxLength(32);
            builder.HasOne<TDecisionRow>().WithMany().HasForeignKey(row => new { row.DecisionId, row.RecordId })
                .HasPrincipalKey(nameof(IReviewDecisionRow.DecisionId), recordIdProperty).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<TDecisionRow>().WithMany().HasForeignKey(row => new { row.ResolvedDecisionId, row.RecordId })
                .HasPrincipalKey(nameof(IReviewDecisionRow.DecisionId), recordIdProperty).OnDelete(DeleteBehavior.Restrict);
        });
    }

}
