using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Simulated Informed identity/income verification. A real integration
/// typically verifies income and employment data (via payroll provider
/// connections or bank transaction analysis) alongside identity — useful
/// in an auto-lending underwriting flow specifically because it can
/// corroborate the income figure the applicant self-reported.
/// </summary>
public class InformedIdentityService : IIdentityVerificationService
{
    public string ProviderName => "Informed";

    public async Task<IdentityVerificationResult> VerifyIdentityAsync(string customerId, string customerName)
    {
        await Task.Delay(130);

        var hash = StableHash.Compute(customerId + "Informed");
        var confirmed = hash % 25 != 0; // ~96% pass rate in this simulation
        var fraudFlag = hash % 55 == 0;

        return new IdentityVerificationResult(
            ProviderName: ProviderName,
            IdentityConfirmed: confirmed,
            FraudFlagRaised: fraudFlag,
            AddressMatch: hash % 16 != 0,
            SsnMatch: confirmed,
            DobMatch: hash % 22 != 0,
            VerificationId: $"INF-{hash:X8}",
            VerifiedAt: DateTimeOffset.UtcNow
        );
    }
}
