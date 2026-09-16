using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Generates a realistic, deterministic set of tradelines (credit report
/// lines) from a seed — shared by both credit bureau implementations, the
/// same reason RiskTierClassifier is shared rather than duplicated: this
/// is bureau-independent mock-data logic, not vendor-specific behavior.
///
/// Deterministic (seeded by hash of the input), not random, so repeated
/// pulls for the same customer during a demo return consistent data —
/// same principle as the credit score generation already used.
/// </summary>
public static class TradelineGenerator
{
    private static readonly string[] Creditors =
        ["Chase", "Capital One", "Discover", "Synchrony Bank", "Toyota Financial", "Wells Fargo", "American Express"];

    public static IReadOnlyList<Tradeline> Generate(int seed)
    {
        var rng = new Random(seed);
        var count = 2 + rng.Next(0, 4); // 2-5 tradelines, realistic for most applicants
        var tradelines = new List<Tradeline>();

        for (var i = 0; i < count; i++)
        {
            var isRevolving = rng.Next(0, 2) == 0;
            var accountType = isRevolving ? "Revolving" : "Installment";
            var creditor = Creditors[rng.Next(Creditors.Length)];

            decimal balance, limitOrOriginal, monthlyPayment;
            if (isRevolving)
            {
                limitOrOriginal = 500 + rng.Next(0, 30) * 500; // $500-$15,000 limit
                balance = Math.Round(limitOrOriginal * (decimal)(rng.NextDouble() * 0.6), 2); // up to 60% utilization
                monthlyPayment = Math.Round(Math.Max(25, balance * 0.02m), 2); // typical min payment ~2%
            }
            else
            {
                limitOrOriginal = 5000 + rng.Next(0, 40) * 1000; // $5,000-$45,000 original amount
                balance = Math.Round(limitOrOriginal * (decimal)(0.2 + rng.NextDouble() * 0.7), 2);
                monthlyPayment = Math.Round(limitOrOriginal / (24 + rng.Next(0, 4) * 12), 2); // rough amortization over 2-5yr
            }

            var paymentStatus = rng.Next(0, 20) == 0 ? "30DaysLate" : "Current"; // ~5% chance of a late mark, realistic-ish

            tradelines.Add(new Tradeline(
                CreditorName: creditor,
                AccountType: accountType,
                Balance: balance,
                CreditLimitOrOriginalAmount: limitOrOriginal,
                MonthlyPayment: monthlyPayment,
                PaymentStatus: paymentStatus,
                OpenedDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-(12 + rng.Next(0, 84))))
            ));
        }

        return tradelines;
    }
}
