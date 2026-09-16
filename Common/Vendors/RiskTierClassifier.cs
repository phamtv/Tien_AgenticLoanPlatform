namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Risk tier classification from a credit score. Extracted here rather
/// than duplicated inside each ICreditBureauService implementation — this
/// is a business rule (FICO score banding), not vendor-specific behavior,
/// so it doesn't belong owned by any one vendor's class. If Experian and
/// TransUnion ever needed genuinely different banding thresholds (some
/// lenders do vary this by bureau), this would become an interface method
/// instead of a shared static — but until there's a real reason for that,
/// one shared implementation is simpler and has one place to fix if the
/// thresholds change.
/// </summary>
public static class RiskTierClassifier
{
    public static string Classify(int creditScore) => creditScore switch
    {
        >= 720 => "Prime",
        >= 620 => "Near-Prime",
        _ => "Subprime",
    };
}
