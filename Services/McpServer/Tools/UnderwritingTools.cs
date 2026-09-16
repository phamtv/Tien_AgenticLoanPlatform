using System.ComponentModel;
using LoanPlatform.McpServer.Http;
using ModelContextProtocol.Server;

namespace LoanPlatform.McpServer.Tools;

/// <summary>
/// Mirrors Services/UnderwritingService/Controllers/UnderwritingController.cs.
/// </summary>
[McpServerToolType]
public class UnderwritingTools
{
    private readonly LoanPlatformApiClient _client;

    public UnderwritingTools(LoanPlatformApiClient client) => _client = client;

    [McpServerTool, Description(
        "Get the full underwriting decision for an application (approval outcome, reason, " +
        "approved amount, interest rate, DTI/LTV, stipulations). Returns 404 if the " +
        "application is still being processed and no decision exists yet.")]
    public Task<ApiResult> GetDecision(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Underwriting, $"api/underwriting/{applicationId}/decision");

    [McpServerTool, Description(
        "Get just the risk figures for an application — credit score, debt-to-income ratio, " +
        "loan-to-value ratio, and the approval outcome — without the full decision payload.")]
    public Task<ApiResult> GetRiskScore(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Underwriting, $"api/underwriting/{applicationId}/risk-score");

    [McpServerTool, Description(
        "Get the underwriter co-pilot's summary for an application — a short, internal-only " +
        "read of the application and its decision: risk factors, inconsistencies, and " +
        "suggested stipulations. This is advisory context for a human underwriter, never a " +
        "lending recommendation, and it's never shown to the applicant. If a summary was " +
        "already generated for this application, this returns that cached copy at no extra " +
        "cost. If none exists yet, this generates one — which calls the Anthropic API and " +
        "has a real cost — exactly like clicking \"Generate summary\" in the review UI does; " +
        "set regenerate=true to force a fresh summary even if a cached one already exists. " +
        "Returns 404 if the application has no underwriting decision on file yet — the " +
        "co-pilot summarizes an existing decision, it doesn't run ahead of one.")]
    public Task<ApiResult> GetCopilotSummary(string applicationId, bool regenerate = false) =>
        _client.PostAsync(LoanPlatformService.Underwriting,
            $"api/underwriting/{applicationId}/copilot-summary{(regenerate ? "?regenerate=true" : "")}");

    [McpServerTool, Description(
        "Run the risk engine for an application and record its underwriting decision — this " +
        "is the ONLY way an application's decision gets made; this platform has no automatic " +
        "event-driven processing. An Approved result does NOT automatically disburse funds — " +
        "call fund_loan or disburse_loan explicitly afterward (typically after also calling " +
        "verify_before_funding) to actually fund an approved application.")]
    public Task<ApiResult> EvaluateApplication(
        string applicationId,
        string customerId,
        decimal requestedAmount,
        int creditScore,
        bool identityConfirmed,
        decimal monthlyIncome,
        decimal existingMonthlyDebt,
        decimal vehicleValue,
        int termMonths = 60,
        string channel = "Online") =>
        _client.PostAsync(LoanPlatformService.Underwriting, $"api/underwriting/{applicationId}/evaluate", new
        {
            customerId,
            requestedAmount,
            creditScore,
            identityConfirmed,
            monthlyIncome,
            existingMonthlyDebt,
            vehicleValue,
            termMonths,
            channel,
        });
}
