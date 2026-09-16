using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Simulated LexisNexis identity/fraud check. A real integration typically
/// calls LexisNexis Risk Solutions' InstantID or FraudPoint API, which
/// cross-references public records, device/behavioral signals, and known
/// fraud consortium data to return a risk score alongside identity
/// confirmation — this demo simplifies that down to a boolean plus a
/// fraud flag.
/// </summary>
public class LexisNexisIdentityService : IIdentityVerificationService
{
    public string ProviderName => "LexisNexis";

    public async Task<IdentityVerificationResult> VerifyIdentityAsync(string customerId, string customerName)
    {
        await Task.Delay(150);

        var hash = StableHash.Compute(customerId + customerName);
        var confirmed = hash % 20 != 0; // ~95% pass rate in this simulation
        var fraudFlag = hash % 50 == 0; // rare simulated fraud flag

        return new IdentityVerificationResult(
            ProviderName: ProviderName,
            IdentityConfirmed: confirmed,
            FraudFlagRaised: fraudFlag,
            AddressMatch: hash % 15 != 0,
            SsnMatch: confirmed, // realistic correlation: SSN mismatch is usually why identity fails at all
            DobMatch: hash % 25 != 0,
            VerificationId: $"LN-{hash:X8}",
            VerifiedAt: DateTimeOffset.UtcNow
        );
    }
}
