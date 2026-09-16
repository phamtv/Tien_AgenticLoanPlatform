using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Simulated TrueID identity verification (document + biometric ID
/// verification — driver's license scan, selfie liveness check). A real
/// integration typically involves the customer's device uploading images
/// directly to TrueID's endpoint (not routed through this backend at all,
/// for latency and PII-handling reasons), with this service polling or
/// receiving a webhook for the final verification result.
/// </summary>
public class TrueIdIdentityService : IIdentityVerificationService
{
    public string ProviderName => "TrueID";

    public async Task<IdentityVerificationResult> VerifyIdentityAsync(string customerId, string customerName)
    {
        await Task.Delay(200); // document/biometric checks are typically slower than a data-only lookup

        var hash = StableHash.Compute(customerId + "TrueID");
        var confirmed = hash % 15 != 0; // ~93% pass rate in this simulation
        var fraudFlag = hash % 60 == 0;

        return new IdentityVerificationResult(
            ProviderName: ProviderName,
            IdentityConfirmed: confirmed,
            FraudFlagRaised: fraudFlag,
            AddressMatch: hash % 18 != 0,
            SsnMatch: confirmed,
            DobMatch: hash % 20 != 0, // biometric/document check cross-references DOB against the scanned ID
            VerificationId: $"TID-{hash:X8}",
            VerifiedAt: DateTimeOffset.UtcNow
        );
    }
}
