using System.ComponentModel;
using LoanPlatform.McpServer.Http;
using ModelContextProtocol.Server;

namespace LoanPlatform.McpServer.Tools;

/// <summary>
/// Mirrors Services/FundingService/Controllers/FundingsController.cs.
/// </summary>
[McpServerToolType]
public class FundingTools
{
    private readonly LoanPlatformApiClient _client;

    public FundingTools(LoanPlatformApiClient client) => _client = client;

    [McpServerTool, Description("List every funded loan.")]
    public Task<ApiResult> ListFundings() =>
        _client.GetAsync(LoanPlatformService.Funding, "api/fundings");

    [McpServerTool, Description("Get the funding record for a specific application, if it has been funded.")]
    public Task<ApiResult> GetFunding(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Funding, $"api/fundings/{applicationId}");

    [McpServerTool, Description("Lightweight funded/not-funded status check for an application, without the full funding record.")]
    public Task<ApiResult> GetFundingStatus(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Funding, $"api/fundings/{applicationId}/status");

    [McpServerTool, Description(
        "Simulate the pre-disbursement identity/title re-verification step for an application " +
        "(a TrueID re-check immediately before funds are released).")]
    public Task<ApiResult> VerifyBeforeFunding(string applicationId) =>
        _client.PostAsync(LoanPlatformService.Funding, $"api/fundings/{applicationId}/verify", new { applicationId });

    [McpServerTool, Description(
        "Manually disburse a loan for an approved application, outside the normal " +
        "event-driven flow. Fails with a conflict if this application has already been funded.")]
    public Task<ApiResult> FundLoan(
        string applicationId, decimal approvedAmount, decimal interestRate,
        int termMonths = 60, string disbursementMethod = "ACH") =>
        _client.PostAsync(LoanPlatformService.Funding, "api/fundings", new
        {
            applicationId,
            approvedAmount,
            interestRate,
            termMonths,
            disbursementMethod,
        });

    [McpServerTool, Description(
        "Identical to FundLoan, but calls the .../disburse alias route on the funding service " +
        "instead of the base POST — same underlying operation, exposed here in case a workflow " +
        "or reference doc specifically expects the disburse verb.")]
    public Task<ApiResult> DisburseLoan(
        string applicationId, decimal approvedAmount, decimal interestRate,
        int termMonths = 60, string disbursementMethod = "ACH") =>
        _client.PostAsync(LoanPlatformService.Funding, $"api/fundings/{applicationId}/disburse", new
        {
            applicationId,
            approvedAmount,
            interestRate,
            termMonths,
            disbursementMethod,
        });
}
