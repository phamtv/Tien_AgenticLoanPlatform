using Microsoft.EntityFrameworkCore;

namespace ServicingService.Data;

public class ServicingDbContext : DbContext
{
    public ServicingDbContext(DbContextOptions<ServicingDbContext> options) : base(options) { }

    public DbSet<LoanEntity> Loans => Set<LoanEntity>();
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoanEntity>(entity =>
        {
            entity.ToTable("Loans");
            entity.HasKey(e => e.LoanId);
            entity.Property(e => e.LoanId).HasMaxLength(20);
            entity.Property(e => e.AccountNumber).HasMaxLength(10);
            entity.HasIndex(e => e.AccountNumber).IsUnique();
            entity.Property(e => e.ApplicationId).HasMaxLength(20);
            entity.HasIndex(e => e.ApplicationId).IsUnique();
            entity.Property(e => e.PrincipalAmount).HasColumnType("decimal(12,2)");
            entity.Property(e => e.InterestRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.CurrentBalance).HasColumnType("decimal(12,2)");
            entity.Property(e => e.PaymentMethod).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(20);
        });

        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(e => e.PaymentId);
            entity.Property(e => e.PaymentId).HasMaxLength(20);
            entity.Property(e => e.LoanId).HasMaxLength(20);
            entity.Property(e => e.Amount).HasColumnType("decimal(12,2)");
            entity.HasOne<LoanEntity>().WithMany().HasForeignKey(e => e.LoanId);
        });
    }
}

public class LoanEntity
{
    public required string LoanId { get; set; }
    public required string AccountNumber { get; set; }
    public required string ApplicationId { get; set; }
    public required decimal PrincipalAmount { get; set; }
    public required decimal InterestRate { get; set; }
    public required int TermMonths { get; set; }
    public required decimal CurrentBalance { get; set; }
    public required string PaymentMethod { get; set; }
    public required int DueDayOfMonth { get; set; }
    public required DateTime OriginatedAt { get; set; }
    public string Status { get; set; } = "Active";
}

public class PaymentEntity
{
    public required string PaymentId { get; set; }
    public required string LoanId { get; set; }
    public required decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
