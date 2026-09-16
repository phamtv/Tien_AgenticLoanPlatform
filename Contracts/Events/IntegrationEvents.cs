namespace LoanPlatform.Contracts.Events;

/// <summary>
/// Base shape every event carries, regardless of which service published it —
/// gives every event a correlation ID (ties the whole loan's journey together
/// across services in logs) and a timestamp, independent of the event's own payload.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Published by Origination once an application has been submitted and vendor
/// checks (credit bureau + identity verification) have completed. Consumed by
/// Underwriting. Carries enough real financial detail (income, debt, vehicle
/// value) for Underwriting to compute actual DTI and LTV ratios, not just
/// react to a bare credit score.
/// </summary>
public record ApplicationReadyForUnderwritingEvent : IntegrationEvent
{
    public required string ApplicationId { get; init; }
    public required string CustomerId { get; init; }
    public required string ApplicantName { get; init; }
    public required decimal RequestedAmount { get; init; }
    public required int TermMonths { get; init; }
    public required string Channel { get; init; } // "Online" | "Dealer" | "Branch"
    public string? DealerName { get; init; }
    public required decimal MonthlyIncome { get; init; }
    public required decimal ExistingMonthlyDebt { get; init; } // from the credit bureau's tradelines — feeds DTI
    public required decimal VehicleValue { get; init; } // feeds LTV
    public required int CreditScore { get; init; }
    public required string CreditBureauUsed { get; init; }
    public required bool IdentityConfirmed { get; init; }
    public required string IdentityProviderUsed { get; init; }
}

/// <summary>
/// Published by Underwriting once a decision has been made. Consumed by
/// Funding (only acts on Approved) and by Origination (to update the
/// application's status either way).
/// </summary>
public record UnderwritingDecisionEvent : IntegrationEvent
{
    public required string ApplicationId { get; init; }
    public required bool Approved { get; init; }
    public required string Reason { get; init; }
    public decimal? ApprovedAmount { get; init; }
    public decimal? InterestRate { get; init; }
    public decimal? DebtToIncomeRatio { get; init; }
    public decimal? LoanToValueRatio { get; init; }
    public IReadOnlyList<string> Stipulations { get; init; } = [];
    // Passed through unchanged from ApplicationReadyForUnderwritingEvent —
    // Underwriting doesn't use these itself, but Funding needs them and
    // shouldn't have to call back to Origination to get them.
    public required int TermMonths { get; init; }
    public required string Channel { get; init; }
    public string? DealerName { get; init; }
}

/// <summary>
/// Published by Funding once a loan has been disbursed. Consumed by
/// Servicing to create the loan record that payments will be tracked against.
/// </summary>
public record LoanFundedEvent : IntegrationEvent
{
    public required string ApplicationId { get; init; }
    public required string LoanId { get; init; }
    public required decimal FundedAmount { get; init; }
    public required decimal InterestRate { get; init; }
    public required int TermMonths { get; init; }
    public required string DisbursementMethod { get; init; }
    public required DateTimeOffset FundedAt { get; init; }
}
