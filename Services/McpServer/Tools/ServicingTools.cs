using System.ComponentModel;
using LoanPlatform.McpServer.Http;
using ModelContextProtocol.Server;

namespace LoanPlatform.McpServer.Tools;

/// <summary>
/// Mirrors Services/ServicingService/Controllers/LoansController.cs.
/// </summary>
[McpServerToolType]
public class ServicingTools
{
    private readonly LoanPlatformApiClient _client;

    public ServicingTools(LoanPlatformApiClient client) => _client = client;

    [McpServerTool(Name = "list_loans"), Description(
        "List every loan in servicing. Note: these records have no customerId — Servicing " +
        "doesn't own customer identity data (that's Origination's), so cross-reference by " +
        "ApplicationId/LoanId if you need to connect a loan back to its original application.")]
    public Task<ApiResult> ListLoans() =>
        _client.GetAsync(LoanPlatformService.Servicing, "api/loans");

    [McpServerTool(Name = "get_loan"), Description("Get full details of a loan by its LoanId (e.g. 'LOAN-D39BC019').")]
    public Task<ApiResult> GetLoan(string loanId) =>
        _client.GetAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}");

    [McpServerTool(Name = "get_loan_balance"), Description("Get just the current outstanding balance for a loan.")]
    public Task<ApiResult> GetLoanBalance(string loanId) =>
        _client.GetAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}/balance");

    [McpServerTool(Name = "get_loan_schedule"), Description(
        "Get the full amortization schedule for a loan, computed from its actual stored " +
        "principal, rate, and term. Optionally override the term to run a what-if scenario " +
        "without changing the stored loan.")]
    public Task<ApiResult> GetLoanSchedule(string loanId, int? termMonths = null)
    {
        var query = termMonths is not null ? $"?termMonths={termMonths}" : "";
        return _client.GetAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}/schedule{query}");
    }

    [McpServerTool(Name = "get_loan_payments"), Description("List every payment recorded against a loan.")]
    public Task<ApiResult> GetLoanPayments(string loanId) =>
        _client.GetAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}/payments");

    [McpServerTool(Name = "make_payment"), Description(
        "Record a payment against a loan. The loan is automatically marked PaidOff if this " +
        "payment brings the balance to exactly zero.")]
    public Task<ApiResult> MakePayment(string loanId, decimal amount) =>
        _client.PostAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}/payments", new { amount });

    [McpServerTool(Name = "update_loan_status"), Description(
        "Update a loan's status. Must be exactly one of: Active, Delinquent, PaidOff. " +
        "Principal, rate, and origination date can't be changed here — those are set once at " +
        "funding time from LoanFundedEvent and are intentionally immutable afterward.")]
    public Task<ApiResult> UpdateLoanStatus(string loanId, string status) =>
        _client.PutAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}", new { status });

    [McpServerTool(Name = "generate_statement"), Description(
        "Generate a statement summary for a loan: total paid to date, current balance, payment " +
        "count, and a rough estimated next payment (not a full amortization lookup).")]
    public Task<ApiResult> GenerateStatement(string loanId) =>
        _client.PostAsync(LoanPlatformService.Servicing, $"api/loans/{loanId}/statements");
}
