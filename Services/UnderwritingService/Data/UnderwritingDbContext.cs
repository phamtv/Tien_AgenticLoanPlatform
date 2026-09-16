using Microsoft.EntityFrameworkCore;

namespace UnderwritingService.Data;

public class UnderwritingDbContext : DbContext
{
    public UnderwritingDbContext(DbContextOptions<UnderwritingDbContext> options) : base(options) { }

    public DbSet<UnderwritingDecisionEntity> UnderwritingDecisions => Set<UnderwritingDecisionEntity>();
    public DbSet<StipulationEntity> Stipulations => Set<StipulationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UnderwritingDecisionEntity>(entity =>
        {
            entity.ToTable("UnderwritingDecisions");
            entity.HasKey(e => e.ApplicationId);
            entity.Property(e => e.ApplicationId).HasMaxLength(20);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.ApprovedAmount).HasColumnType("decimal(12,2)");
            entity.Property(e => e.InterestRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DebtToIncomeRatio).HasColumnType("decimal(5,4)");
            entity.Property(e => e.LoanToValueRatio).HasColumnType("decimal(5,4)");
            // No explicit length on CopilotSummaryJson — it's a JSON blob
            // (the co-pilot's structured summary), so this maps to EF
            // Core's SQL Server default of nvarchar(max), same treatment
            // ExtractedDataJson gets on OriginationService's document
            // entity for the same reason.
        });

        modelBuilder.Entity<StipulationEntity>(entity =>
        {
            entity.ToTable("Stipulations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).HasMaxLength(300);
            entity.HasOne<UnderwritingDecisionEntity>().WithMany().HasForeignKey(e => e.ApplicationId);
        });
    }
}

public class UnderwritingDecisionEntity
{
    public required string ApplicationId { get; set; }
    public required bool Approved { get; set; }
    public required string Reason { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? InterestRate { get; set; }
    public required int CreditScore { get; set; }
    public required decimal DebtToIncomeRatio { get; set; }
    public required decimal LoanToValueRatio { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

    // BUG FIX: set to true once Funding has actually disbursed a loan
    // against this decision (see LoanFundedEventHandler in
    // UnderwritingService/EventHandlers.cs). UnderwritingController.Evaluate
    // refuses to re-evaluate — and therefore overwrite — a decision once
    // this is true. Before this flag existed, a later re-evaluation (the
    // manual /evaluate endpoint, or a duplicate automatic evaluation) could
    // silently replace an Approved decision that Funding had already acted
    // on with a Denied one, leaving a real funded loan with no trace in
    // Underwriting's own records that it had ever been approved.
    public bool Funded { get; set; } = false;

    // Underwriter co-pilot (see Common/AI/ClaudeUnderwritingCopilotService.cs
    // and UnderwritingController.GetCopilotSummary). Both null until
    // someone manually requests a summary for this application — this is
    // never populated automatically, specifically so the Anthropic API
    // isn't called for every application that passes through underwriting,
    // only the ones a human actually opens and asks about.
    public string? CopilotSummaryJson { get; set; }
    public DateTime? CopilotGeneratedAt { get; set; }
}

public class StipulationEntity
{
    public int Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string Description { get; set; }
}
