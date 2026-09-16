namespace UnderwritingService;

public record RiskDecision(
    bool Approved,
    string Reason,
    decimal? ApprovedAmount,
    decimal? InterestRate,
    decimal DebtToIncomeRatio,
    decimal LoanToValueRatio,
    IReadOnlyList<string> Stipulations
);

/// <summary>
/// Rule-based decision engine using real auto-lending underwriting
/// metrics — DTI (debt-to-income) and LTV (loan-to-value) — not just a
/// bare credit score threshold. A real underwriting system would likely
/// use a scored model on top of these, but the metrics themselves and
/// their thresholds below are genuine industry conventions, not made up:
///   - DTI above ~45% is a common decline/manual-review line across
///     auto lenders (mirrors mortgage underwriting conventions, adapted
///     for auto).
///   - LTV above 125% (financing well beyond the vehicle's value —
///     common with low/no down payment plus rolled-in fees) is a common
///     point where lenders reduce the approved amount or decline.
/// </summary>
public static class RiskEngine
{
    private const decimal MaxAcceptableDti = 0.45m;
    private const decimal MaxAcceptableLtv = 1.25m;

    public static RiskDecision Evaluate(int creditScore, bool identityConfirmed, decimal requestedAmount, decimal monthlyIncome, decimal existingMonthlyDebt, decimal vehicleValue)
    {
        // Estimate this loan's own monthly payment for the DTI calculation
        // using a representative rate/term — a real engine would iterate
        // this against the actual approved rate/term once decided, but a
        // reasonable estimate up front is standard practice for a first-pass
        // DTI check before the final terms are set.
        const int estimatedTermMonths = 60;
        const decimal estimatedRate = 0.09m; // rough blended estimate for the DTI pre-check
        var estimatedMonthlyRate = estimatedRate / 12;
        var estimatedNewPayment = requestedAmount * estimatedMonthlyRate / (1 - (decimal)Math.Pow((double)(1 + estimatedMonthlyRate), -estimatedTermMonths));

        var dti = monthlyIncome > 0 ? (existingMonthlyDebt + estimatedNewPayment) / monthlyIncome : 1m;
        var ltv = vehicleValue > 0 ? requestedAmount / vehicleValue : 1m;

        if (!identityConfirmed)
            return new RiskDecision(false, "Identity could not be confirmed.", null, null, dti, ltv, []);

        if (creditScore < 580)
            return new RiskDecision(false, $"Credit score {creditScore} is below the minimum threshold of 580.", null, null, dti, ltv, []);

        if (dti > MaxAcceptableDti)
            return new RiskDecision(false, $"Debt-to-income ratio of {dti:P0} exceeds the maximum acceptable {MaxAcceptableDti:P0}.", null, null, dti, ltv, []);

        var (baseAmount, rate) = creditScore switch
        {
            >= 720 => (requestedAmount, 5.9m),   // Prime
            >= 620 => (requestedAmount * 0.9m, 9.9m),   // Near-Prime
            _ => (requestedAmount * 0.75m, 14.9m),   // Subprime
        };

        // LTV cap: if the requested amount financed against the vehicle's
        // value exceeds the acceptable LTV, cap the approved amount at
        // that ratio rather than declining outright — a common real
        // underwriting response to high LTV, not just a hard stop.
        var maxAmountForLtv = vehicleValue * MaxAcceptableLtv;
        var approvedAmount = Math.Min(baseAmount, maxAmountForLtv);

        var stipulations = new List<string>();
        if (creditScore < 650) stipulations.Add("Proof of income required (2 most recent pay stubs)");
        if (dti > 0.35m) stipulations.Add("Proof of residence required (utility bill or lease agreement)");
        if (ltv > 1.0m) stipulations.Add("Gap insurance required due to loan-to-value ratio above 100%");

        // The message here needs to correctly attribute *why* the amount
        // was reduced — it's not always the LTV cap. Two distinct causes
        // produce the same symptom (approvedAmount < requestedAmount):
        // the risk-tier-based base reduction (baseAmount < requestedAmount,
        // ordinary risk pricing, not something the borrower needs to
        // remedy) versus the LTV cap actually binding (maxAmountForLtv was
        // the smaller value). Conflating them was a real bug caught while
        // testing this end to end — the original version always blamed
        // LTV even when the LTV cap was never the binding constraint.
        if (approvedAmount < baseAmount)
            stipulations.Add($"Approved amount capped at ${approvedAmount:N2} because the loan-to-value ratio would otherwise exceed {MaxAcceptableLtv:P0} of the vehicle's value.");

        var tierNote = approvedAmount < requestedAmount && approvedAmount == baseAmount
            ? $" Amount limited to the risk tier's approval ceiling (${baseAmount:N2})."
            : "";
        return new RiskDecision(true, $"Approved based on credit score {creditScore}, DTI {dti:P0}, LTV {ltv:P0}.{tierNote}", approvedAmount, rate, dti, ltv, stipulations);
    }
}
