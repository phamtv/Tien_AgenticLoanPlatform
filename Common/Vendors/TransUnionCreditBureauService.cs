using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Simulated TransUnion credit pull. A real integration would call
/// TransUnion's TLOxp or DecisionEdge API — typically requiring a signed
/// FCRA permissible-purpose certification per request, on top of the
/// usual auth. That compliance requirement (not just the API call itself)
/// is often the harder part of a real TransUnion integration.
/// </summary>
public class TransUnionCreditBureauService : ICreditBureauService
{
    public string BureauName => "TransUnion";

    public async Task<CreditBureauResult> PullCreditReportAsync(string customerId, string firstName, string lastName, string ssnLastFour)
    {
        await Task.Delay(110);

        var seed = StableHash.Compute(customerId + "TU");
        var score = 560 + (seed % 281); // 560-840

        var tradelines = TradelineGenerator.Generate(seed);
        var totalMonthlyDebt = tradelines.Sum(t => t.MonthlyPayment);

        var rng = new Random(seed);
        var inquiries = rng.Next(0, 6);
        var hasBankruptcy = score < 600 && rng.Next(0, 8) == 0;
        var hasLien = score < 620 && rng.Next(0, 12) == 0;

        return new CreditBureauResult(
            BureauName: BureauName,
            ScoreModel: "VantageScore 4.0", // TransUnion's own model family, a realistic point of contrast against Experian's FICO-based score above
            CreditScore: score,
            RiskTier: RiskTierClassifier.Classify(score),
            Tradelines: tradelines,
            InquiriesLast6Months: inquiries,
            TotalMonthlyDebtPayments: totalMonthlyDebt,
            HasBankruptcy: hasBankruptcy,
            HasLien: hasLien,
            PulledAt: DateTimeOffset.UtcNow
        );
    }
}
