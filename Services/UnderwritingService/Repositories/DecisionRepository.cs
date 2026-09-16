using System.Collections.Concurrent;

namespace UnderwritingService.Repositories;

public record UnderwritingRecord(
    string ApplicationId,
    bool Approved,
    string Reason,
    decimal? ApprovedAmount,
    decimal? InterestRate,
    int CreditScore,
    decimal DebtToIncomeRatio,
    decimal LoanToValueRatio,
    IReadOnlyList<string> Stipulations,
    DateTimeOffset DecidedAt,
    // BUG FIX: see the matching comment on UnderwritingDecisionEntity.Funded
    // in Data/UnderwritingDbContext.cs — defaults to false because a
    // decision is never created already-funded; it's flipped true later,
    // in place, via MarkFunded() once Funding actually acts on it.
    bool Funded = false,
    // Underwriter co-pilot: the last generated summary, cached in place so
    // re-opening the same application doesn't call Claude (and incur cost)
    // again — see UnderwritingController.GetCopilotSummary and
    // IDecisionRepository.UpdateCopilotSummary below. Null until the first
    // time someone actually clicks "Generate summary" for this application;
    // this is a manual, on-demand feature, never generated automatically.
    string? CopilotSummaryJson = null,
    DateTimeOffset? CopilotGeneratedAt = null
);

public interface IDecisionRepository
{
    UnderwritingRecord Add(UnderwritingRecord record);
    UnderwritingRecord? GetByApplicationId(string applicationId);

    /// <summary>
    /// Marks an existing decision as funded, without touching any of its
    /// other fields. Called by LoanFundedEventHandler once Funding has
    /// actually disbursed a loan against this application. After this,
    /// UnderwritingController.Evaluate refuses to re-evaluate (and
    /// therefore overwrite) this decision — see the bug this closes in
    /// the class remarks on UnderwritingDecisionEntity.Funded.
    /// </summary>
    void MarkFunded(string applicationId);

    /// <summary>
    /// Stores the underwriter co-pilot's generated summary against an
    /// existing decision, in place — deliberately NOT routed through
    /// Add()/Remove(), same reasoning as MarkFunded: re-adding would also
    /// wipe and re-insert the stipulations for no reason. No-ops if no
    /// decision exists yet for this application (shouldn't happen in
    /// practice — the controller only calls this after confirming a
    /// decision record exists).
    /// </summary>
    void UpdateCopilotSummary(string applicationId, string summaryJson, DateTimeOffset generatedAt);
}

public class InMemoryDecisionRepository : IDecisionRepository
{
    private readonly ConcurrentDictionary<string, UnderwritingRecord> _decisions = new();

    public UnderwritingRecord Add(UnderwritingRecord record)
    {
        _decisions[record.ApplicationId] = record;
        return record;
    }

    public UnderwritingRecord? GetByApplicationId(string applicationId) =>
        _decisions.GetValueOrDefault(applicationId);

    public void MarkFunded(string applicationId)
    {
        if (_decisions.TryGetValue(applicationId, out var existing))
        {
            _decisions[applicationId] = existing with { Funded = true };
        }
        // If no decision exists yet, there's nothing to mark — this
        // shouldn't happen in practice (Funding only acts on an Approved
        // decision it already received), but silently no-op rather than
        // throw, consistent with how the rest of this event-driven system
        // treats a missing/late counterpart elsewhere.
    }

    public void UpdateCopilotSummary(string applicationId, string summaryJson, DateTimeOffset generatedAt)
    {
        if (_decisions.TryGetValue(applicationId, out var existing))
        {
            _decisions[applicationId] = existing with { CopilotSummaryJson = summaryJson, CopilotGeneratedAt = generatedAt };
        }
    }
}
