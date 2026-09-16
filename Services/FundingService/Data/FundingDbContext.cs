using Microsoft.EntityFrameworkCore;

namespace FundingService.Data;

public class FundingDbContext : DbContext
{
    public FundingDbContext(DbContextOptions<FundingDbContext> options) : base(options) { }

    public DbSet<FundingEntity> Fundings => Set<FundingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FundingEntity>(entity =>
        {
            entity.ToTable("Fundings");
            entity.HasKey(e => e.LoanId);
            entity.Property(e => e.LoanId).HasMaxLength(20);
            entity.Property(e => e.ApplicationId).HasMaxLength(20);
            entity.HasIndex(e => e.ApplicationId).IsUnique();
            entity.Property(e => e.FundedAmount).HasColumnType("decimal(12,2)");
            entity.Property(e => e.InterestRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DisbursementMethod).HasMaxLength(20);
        });
    }
}

public class FundingEntity
{
    public required string LoanId { get; set; }
    public required string ApplicationId { get; set; }
    public required decimal FundedAmount { get; set; }
    public required decimal InterestRate { get; set; }
    public required int TermMonths { get; set; }
    public required string DisbursementMethod { get; set; }
    public DateTime FundedAt { get; set; } = DateTime.UtcNow;
}
