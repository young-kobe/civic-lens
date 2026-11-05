using CivicLens.Application.Collection.Bills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillActionRecordedVoteRowConfiguration : IEntityTypeConfiguration<BillActionRecordedVoteRow>
{
    public void Configure(EntityTypeBuilder<BillActionRecordedVoteRow> builder)
    {
        builder.ToTable("bill_action_recorded_votes", table =>
        {
            table.HasCheckConstraint("ck_bill_action_recorded_votes_position", "action_position >= 0 AND position >= 0");
            table.HasCheckConstraint("ck_bill_action_recorded_votes_roll",
                "congress >= 1 AND session_number IN (1, 2) AND roll_number >= 1");
            table.HasCheckConstraint("ck_bill_action_recorded_votes_chamber", "chamber IN ('House', 'Senate')");
            table.HasCheckConstraint("ck_bill_action_recorded_votes_url",
                $"url IS NULL OR url LIKE '{CongressBillActionsParser.UrlPrefix}%'");
        });
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.ActionPosition).HasColumnName("action_position");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Chamber).HasColumnName("chamber").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.SessionNumber).HasColumnName("session_number");
        builder.Property(row => row.RollNumber).HasColumnName("roll_number");
        builder.Property(row => row.VotedAtUtcTicks).HasColumnName("voted_at_utc_ticks");
        builder.Property(row => row.Url).HasColumnName("url");
        builder.HasKey(row => new { row.DetailJobId, row.ActionPosition, row.Position });
        builder.HasOne<BillActionRow>().WithMany().HasForeignKey(row => new { row.DetailJobId, row.ActionPosition })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
