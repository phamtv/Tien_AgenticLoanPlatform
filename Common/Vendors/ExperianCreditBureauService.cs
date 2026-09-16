using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Simulated Experian credit pull. A real integration would call
/// Experian's Connect API (OAuth2 client-credentials auth, typically a
/// SOAP or REST endpoint depending on the specific product — Experian
/// offers several), passing the applicant's SSN, name, and address for a
/// soft/hard pull, and parsing back a full credit report: score,
/// tradelines, inquiries, and public records — which is what this class
/// actually simulates now, not just a bare score.
/// </summary>
public class ExperianCreditBureauService : ICreditBureauService
{
    public string BureauName => "Experian";

    public async Task<CreditBureauResult> PullCreditReportAsync(string customerId, string firstName, string lastName, string ssnLastFour)
    {
        // Simulates real network latency a bureau call would have.
        await Task.Delay(120);

        var seed = StableHash.Compute(customerId);
        var score = 580 + (seed % 261); // 580-840 range

        var tradelines = TradelineGenerator.Generate(seed);
        var totalMonthlyDebt = tradelines.Sum(t => t.MonthlyPayment);

        var rng = new Random(seed);
        var inquiries = rng.Next(0, 6);
        var hasBankruptcy = score < 600 && rng.Next(0, 8) == 0; // rare, and only realistic at low scores
        var hasLien = score < 620 && rng.Next(0, 12) == 0;

        return new CreditBureauResult(
            BureauName: BureauName,
            ScoreModel: "FICO Auto Score 8", // auto lenders commonly use an auto-specific FICO model, not the generic consumer score
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
