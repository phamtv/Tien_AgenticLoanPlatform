using UnderwritingService.Data;

namespace UnderwritingService.Repositories;

public class SqlDecisionRepository : IDecisionRepository
{
    private readonly UnderwritingDbContext _db;

    public SqlDecisionRepository(UnderwritingDbContext db)
    {
        _db = db;
    }

    public UnderwritingRecord Add(UnderwritingRecord record)
    {
        var existing = _db.UnderwritingDecisions.Find(record.ApplicationId);
        if (existing is not null)
        {
            _db.UnderwritingDecisions.Remove(existing);
            // Existing stipulations tied to this application also need
            // clearing before re-adding — a re-evaluation (e.g. via the
            // manual /evaluate endpoint) replaces the prior decision
            // entirely rather than accumulating duplicate stipulation rows.
            var oldStipulations = _db.Stipulations.Where(s => s.ApplicationId == record.ApplicationId);
            _db.Stipulations.RemoveRange(oldStipulations);
        }

        _db.UnderwritingDecisions.Add(new UnderwritingDecisionEntity
        {
            ApplicationId = record.ApplicationId,
            Approved = record.Approved,
            Reason = record.Reason,
            ApprovedAmount = record.ApprovedAmount,
            InterestRate = record.InterestRate,
            CreditScore = record.CreditScore,
            DebtToIncomeRatio = record.DebtToIncomeRatio,
            LoanToValueRatio = record.LoanToValueRatio,
            DecidedAt = record.DecidedAt.UtcDateTime,
            // Always false here — Add() only ever runs for a brand-new or
            // re-evaluated decision, and UnderwritingController.Evaluate
            // now refuses to re-evaluate a decision once Funded is true
            // (see that Conflict check), so Add() is never reached for an
            // application that's already been funded.
            Funded = record.Funded,
            // A re-evaluation intentionally clears any prior co-pilot
            // summary rather than carrying it forward — the summary was
            // generated against the OLD decision/risk figures, and keeping
            // it around against a freshly re-evaluated decision would be
            // stale, misleading context for whoever reads it next.
            CopilotSummaryJson = null,
            CopilotGeneratedAt = null,
        });

        foreach (var stip in record.Stipulations)
        {
            _db.Stipulations.Add(new StipulationEntity { ApplicationId = record.ApplicationId, Description = stip });
        }

        _db.SaveChanges();
        return record;
    }

    public UnderwritingRecord? GetByApplicationId(string applicationId)
    {
        var entity = _db.UnderwritingDecisions.Find(applicationId);
        if (entity is null) return null;

        var stipulations = _db.Stipulations
            .Where(s => s.ApplicationId == applicationId)
            .Select(s => s.Description)
            .ToList();

        return new UnderwritingRecord(
            entity.ApplicationId, entity.Approved, entity.Reason, entity.ApprovedAmount, entity.InterestRate,
            entity.CreditScore, entity.DebtToIncomeRatio, entity.LoanToValueRatio, stipulations, entity.DecidedAt,
            entity.Funded, entity.CopilotSummaryJson,
            entity.CopilotGeneratedAt is null ? null : new DateTimeOffset(entity.CopilotGeneratedAt.Value, TimeSpan.Zero));
    }

    /// <summary>
    /// Updates just the Funded flag on an existing decision row, in place —
    /// deliberately NOT routed through Add()/Remove(), since that would
    /// also wipe and re-insert the stipulations for no reason. See the bug
    /// this exists to close in the remarks on UnderwritingDecisionEntity.Funded.
    /// </summary>
    public void MarkFunded(string applicationId)
    {
        var existing = _db.UnderwritingDecisions.Find(applicationId);
        if (existing is null) return; // nothing to mark — see InMemoryDecisionRepository's matching comment

        existing.Funded = true;
        _db.SaveChanges();
    }

    /// <summary>
    /// Updates just the co-pilot summary fields on an existing decision
    /// row, in place — same reasoning as MarkFunded. No-ops if the
    /// decision doesn't exist (shouldn't happen — see interface remarks).
    /// </summary>
    public void UpdateCopilotSummary(string applicationId, string summaryJson, DateTimeOffset generatedAt)
    {
        var existing = _db.UnderwritingDecisions.Find(applicationId);
        if (existing is null) return;

        existing.CopilotSummaryJson = summaryJson;
        existing.CopilotGeneratedAt = generatedAt.UtcDateTime;
        _db.SaveChanges();
    }
}
