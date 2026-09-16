namespace LoanPlatform.Contracts.Models;

/// <summary>
/// Applicant demographics — the fields a real LOS actually collects
/// beyond just a name, matching what Origination needs to run a credit
/// pull and identity check against.
/// </summary>
public record Applicant(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string SsnLastFour, // never store/transmit a full SSN in a demo — same principle as not logging secrets
    string Email,
    string Phone,
    string AddressLine1,
    string City,
    string State,
    string ZipCode
);

/// <summary>Employment info — required for DTI (debt-to-income) calculation in underwriting.</summary>
public record Employment(
    string EmployerName,
    string JobTitle,
    decimal MonthlyIncome,
    int EmploymentMonths
);

/// <summary>Vehicle being financed — LTV (loan-to-value) is computed from SalePrice vs. the requested loan amount.</summary>
public record Vehicle(
    int Year,
    string Make,
    string Model,
    string Vin,
    int Mileage,
    string Condition, // "New" | "Used"
    decimal SalePrice
);

public record LoanApplication(
    string ApplicationId,
    string CustomerId,
    Applicant Applicant,
    Employment Employment,
    Vehicle Vehicle,
    decimal RequestedAmount,
    decimal DownPayment,
    int TermMonths,
    string Channel, // "Online" | "Dealer" | "Branch" — how the application was submitted, same as a real LOS tracks
    string? DealerName,
    string Status, // "Submitted" | "UnderwritingInProgress" | "Approved" | "Denied" | "Funded"
    DateTimeOffset SubmittedAt
);

/// <summary>
/// One line on a credit report — a real bureau pull returns a list of
/// these (tradelines), not just a single score. Used here to compute
/// TotalMonthlyDebtPayments for a real DTI calculation in underwriting,
/// not a made-up number.
/// </summary>
public record Tradeline(
    string CreditorName,
    string AccountType, // "Revolving" | "Installment" | "Mortgage"
    decimal Balance,
    decimal CreditLimitOrOriginalAmount,
    decimal MonthlyPayment,
    string PaymentStatus, // "Current" | "30DaysLate" | "60DaysLate" | "Charged Off"
    DateOnly OpenedDate
);

public record CreditBureauResult(
    string BureauName,
    string ScoreModel, // e.g. "FICO Auto Score 8" — real bureau pulls specify which scoring model was used, since auto lenders often use an auto-specific model rather than a generic FICO score
    int CreditScore,
    string RiskTier, // "Prime" | "Near-Prime" | "Subprime"
    IReadOnlyList<Tradeline> Tradelines,
    int InquiriesLast6Months,
    decimal TotalMonthlyDebtPayments, // sum of tradeline MonthlyPayment — feeds directly into DTI in underwriting
    bool HasBankruptcy,
    bool HasLien,
    DateTimeOffset PulledAt
);

public record IdentityVerificationResult(
    string ProviderName,
    bool IdentityConfirmed,
    bool FraudFlagRaised,
    bool AddressMatch,
    bool SsnMatch,
    bool DobMatch,
    string VerificationId,
    DateTimeOffset VerifiedAt
);
