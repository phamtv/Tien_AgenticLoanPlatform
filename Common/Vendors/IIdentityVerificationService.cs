using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Every identity/fraud verification integration implements this — same
/// pattern as ICreditBureauService, so origination logic depends on the
/// abstraction, not any specific vendor.
/// </summary>
public interface IIdentityVerificationService
{
    string ProviderName { get; }
    Task<IdentityVerificationResult> VerifyIdentityAsync(string customerId, string customerName);
}
