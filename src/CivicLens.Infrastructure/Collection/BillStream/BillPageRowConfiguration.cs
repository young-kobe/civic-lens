using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillPageRowConfiguration : IEntityTypeConfiguration<BillPageRow>
{
    public void Configure(EntityTypeBuilder<BillPageRow> builder)
    {
        builder.ToTable("bill_pages", table =>
        {
            table.HasCheckConstraint("ck_bill_pages_order", "page_order >= 0");
            table.HasCheckConstraint("ck_bill_pages_role", "role IN ('Detail','Cosponsors','Actions','Summaries','Subjects')");
            table.HasCheckConstraint("ck_bill_pages_detail",
                "role <> 'Detail' OR (job_id = detail_job_id AND page_order = 0 AND closes_chain)");
        });
        builder.Property(row => row.JobId).HasColumnName("job_id").HasMaxLength(128);
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.Role).HasColumnName("role").HasMaxLength(32).HasConversion<string>();
        builder.Property(row => row.PageOrder).HasColumnName("page_order");
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64);
        builder.Property(row => row.ClosesChain).HasColumnName("closes_chain");
        builder.HasKey(row => row.JobId);
        builder.HasIndex(row => new { row.DetailJobId, row.Role, row.PageOrder }).IsUnique().HasDatabaseName("ux_bill_pages_chain_order");
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillGateRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.ExtractionId).OnDelete(DeleteBehavior.Restrict);
    }
}
