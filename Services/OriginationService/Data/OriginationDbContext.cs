using Microsoft.EntityFrameworkCore;

namespace OriginationService.Data;

/// <summary>
/// Real EF Core DbContext, matching Database/01_origination_schema.sql
/// exactly. Requires Microsoft.EntityFrameworkCore.SqlServer — see this
/// project's .csproj, where these files are only compiled in when built
/// with -p:UseSqlServer=true (the default in-memory build doesn't
/// reference this at all, so it stays buildable without the SQL Server
/// NuGet packages installed).
/// </summary>
public class OriginationDbContext : DbContext
{
    public OriginationDbContext(DbContextOptions<OriginationDbContext> options) : base(options) { }

    public DbSet<LoanApplicationEntity> LoanApplications => Set<LoanApplicationEntity>();
    public DbSet<CreditBureauResultEntity> CreditBureauResults => Set<CreditBureauResultEntity>();
    public DbSet<TradelineEntity> Tradelines => Set<TradelineEntity>();
    public DbSet<IdentityVerificationResultEntity> IdentityVerificationResults => Set<IdentityVerificationResultEntity>();
    public DbSet<ApplicationDocumentEntity> ApplicationDocuments => Set<ApplicationDocumentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoanApplicationEntity>(entity =>
        {
            entity.ToTable("LoanApplications");
            entity.HasKey(e => e.ApplicationId);
            entity.Property(e => e.ApplicationId).HasMaxLength(20);
            entity.Property(e => e.CustomerId).HasMaxLength(50);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.SsnLastFour).HasMaxLength(4);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.AddressLine1).HasMaxLength(200);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.ZipCode).HasMaxLength(10);
            entity.Property(e => e.EmployerName).HasMaxLength(200);
            entity.Property(e => e.JobTitle).HasMaxLength(100);
            entity.Property(e => e.MonthlyIncome).HasColumnType("decimal(12,2)");
            entity.Property(e => e.VehicleMake).HasMaxLength(50);
            entity.Property(e => e.VehicleModel).HasMaxLength(50);
            entity.Property(e => e.VehicleVin).HasMaxLength(17);
            entity.Property(e => e.VehicleCondition).HasMaxLength(10);
            entity.Property(e => e.VehicleSalePrice).HasColumnType("decimal(12,2)");
            entity.Property(e => e.RequestedAmount).HasColumnType("decimal(12,2)");
            entity.Property(e => e.DownPayment).HasColumnType("decimal(12,2)");
            entity.Property(e => e.Channel).HasMaxLength(20);
            entity.Property(e => e.DealerName).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<CreditBureauResultEntity>(entity =>
        {
            entity.ToTable("CreditBureauResults");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BureauName).HasMaxLength(30);
            entity.Property(e => e.ScoreModel).HasMaxLength(50);
            entity.Property(e => e.RiskTier).HasMaxLength(20);
            entity.Property(e => e.TotalMonthlyDebtPayments).HasColumnType("decimal(12,2)");
            entity.HasOne<LoanApplicationEntity>().WithMany().HasForeignKey(e => e.ApplicationId);
        });

        modelBuilder.Entity<TradelineEntity>(entity =>
        {
            entity.ToTable("Tradelines");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreditorName).HasMaxLength(100);
            entity.Property(e => e.AccountType).HasMaxLength(20);
            entity.Property(e => e.Balance).HasColumnType("decimal(12,2)");
            entity.Property(e => e.CreditLimitOrOriginalAmount).HasColumnType("decimal(12,2)");
            entity.Property(e => e.MonthlyPayment).HasColumnType("decimal(12,2)");
            entity.Property(e => e.PaymentStatus).HasMaxLength(20);
            entity.HasOne<CreditBureauResultEntity>().WithMany().HasForeignKey(e => e.CreditBureauResultId);
        });

        modelBuilder.Entity<IdentityVerificationResultEntity>(entity =>
        {
            entity.ToTable("IdentityVerificationResults");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderName).HasMaxLength(30);
            entity.Property(e => e.VerificationId).HasMaxLength(50);
            entity.HasOne<LoanApplicationEntity>().WithMany().HasForeignKey(e => e.ApplicationId);
        });

        modelBuilder.Entity<ApplicationDocumentEntity>(entity =>
        {
            entity.ToTable("ApplicationDocuments");
            entity.HasKey(e => e.DocumentId);
            entity.Property(e => e.DocumentId).HasMaxLength(20);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.HasOne<LoanApplicationEntity>().WithMany().HasForeignKey(e => e.ApplicationId);
        });
    }
}
