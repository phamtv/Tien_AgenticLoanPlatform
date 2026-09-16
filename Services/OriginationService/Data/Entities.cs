namespace OriginationService.Data;

// Entity classes are kept separate from the Contracts.Models records used
// on the wire — the database shape and the API/event contract shape are
// allowed to diverge over time without that leaking into every service
// that consumes the API.

public class LoanApplicationEntity
{
    public required string ApplicationId { get; set; }
    public required string CustomerId { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required DateOnly DateOfBirth { get; set; }
    public required string SsnLastFour { get; set; }
    public required string Email { get; set; }
    public required string Phone { get; set; }
    public required string AddressLine1 { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string ZipCode { get; set; }

    public required string EmployerName { get; set; }
    public required string JobTitle { get; set; }
    public required decimal MonthlyIncome { get; set; }
    public required int EmploymentMonths { get; set; }

    public required int VehicleYear { get; set; }
    public required string VehicleMake { get; set; }
    public required string VehicleModel { get; set; }
    public required string VehicleVin { get; set; }
    public required int VehicleMileage { get; set; }
    public required string VehicleCondition { get; set; }
    public required decimal VehicleSalePrice { get; set; }

    public required decimal RequestedAmount { get; set; }
    public required decimal DownPayment { get; set; }
    public required int TermMonths { get; set; }
    public required string Channel { get; set; }
    public string? DealerName { get; set; }

    public required string Status { get; set; }
    public required DateTime SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CreditBureauResultEntity
{
    public int Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string BureauName { get; set; }
    public required string ScoreModel { get; set; }
    public required int CreditScore { get; set; }
    public required string RiskTier { get; set; }
    public required int InquiriesLast6Months { get; set; }
    public required decimal TotalMonthlyDebtPayments { get; set; }
    public required bool HasBankruptcy { get; set; }
    public required bool HasLien { get; set; }
    public DateTime PulledAt { get; set; }
}

public class TradelineEntity
{
    public int Id { get; set; }
    public required int CreditBureauResultId { get; set; }
    public required string CreditorName { get; set; }
    public required string AccountType { get; set; }
    public required decimal Balance { get; set; }
    public required decimal CreditLimitOrOriginalAmount { get; set; }
    public required decimal MonthlyPayment { get; set; }
    public required string PaymentStatus { get; set; }
    public required DateOnly OpenedDate { get; set; }
}

public class IdentityVerificationResultEntity
{
    public int Id { get; set; }
    public required string ApplicationId { get; set; }
    public required string ProviderName { get; set; }
    public required bool IdentityConfirmed { get; set; }
    public required bool FraudFlagRaised { get; set; }
    public required bool AddressMatch { get; set; }
    public required bool SsnMatch { get; set; }
    public required bool DobMatch { get; set; }
    public required string VerificationId { get; set; }
    public DateTime VerifiedAt { get; set; }
}

public class ApplicationDocumentEntity
{
    public required string DocumentId { get; set; }
    public required string ApplicationId { get; set; }
    public required string FileName { get; set; }
    public required string DocumentType { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Verified (user-reviewed) extraction result as a JSON string, once
    /// this document has gone through the Claude extraction flow. Left
    /// unconstrained (no HasMaxLength in OriginationDbContext) since it's
    /// a JSON blob, not a bounded text field — same reasoning as not
    /// putting a length limit on other free-form JSON payloads elsewhere
    /// in this project.
    /// </summary>
    public string? ExtractedDataJson { get; set; }
}
