using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionBillNumberRowConfiguration : IEntityTypeConfiguration<StatementRevisionBillNumberRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionBillNumberRow> builder)
    {
        builder.ToTable("statement_revision_bill_numbers", table =>
        {
            table.HasCheckConstraint("ck_statement_revision_bill_numbers_position", "position >= 0");
            table.HasCheckConstraint("ck_statement_revision_bill_numbers_bill_number", "length(btrim(bill_number)) > 0");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber, row.Position });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.BillNumber).HasColumnName("bill_number").IsRequired();
        builder.HasIndex(row => row.BillNumber);
    }
}
