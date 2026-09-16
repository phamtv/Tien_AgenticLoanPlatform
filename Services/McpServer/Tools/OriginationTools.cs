using System.ComponentModel;
using LoanPlatform.McpServer.Http;
using ModelContextProtocol.Server;

namespace LoanPlatform.McpServer.Tools;

/// <summary>
/// Mirrors Services/OriginationService/Controllers/ApplicationsController.cs
/// one tool per endpoint. SubmitApplication's parameters are flattened
/// (not nested Applicant/Employment/Vehicle objects) deliberately — flat
/// scalar parameters are the safest bet for automatic JSON-schema
/// generation across MCP SDK versions; the nested shape the real
/// SubmitApplicationRequest expects is reconstructed inside the method
/// before the HTTP call goes out.
/// </summary>
[McpServerToolType]
public class OriginationTools
{
    private readonly LoanPlatformApiClient _client;

    public OriginationTools(LoanPlatformApiClient client) => _client = client;

    [McpServerTool, Description("List every loan application known to the Origination service.")]
    public Task<ApiResult> ListApplications() =>
        _client.GetAsync(LoanPlatformService.Origination, "api/applications");

    [McpServerTool, Description("Get full details of one application by its ApplicationId (e.g. 'APP-1A2B3C4D').")]
    public Task<ApiResult> GetApplication(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Origination, $"api/applications/{applicationId}");

    [McpServerTool, Description(
        "Get just the status of an application — e.g. Submitted, UnderwritingInProgress, " +
        "Approved, Denied, or Funded.")]
    public Task<ApiResult> GetApplicationStatus(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Origination, $"api/applications/{applicationId}/status");

    [McpServerTool, Description(
        "Update the requested amount and/or term on an application. Fails with a conflict if " +
        "the application has already been Approved, Denied, or Funded.")]
    public Task<ApiResult> UpdateApplication(string applicationId, decimal? requestedAmount = null, int? termMonths = null) =>
        _client.PutAsync(LoanPlatformService.Origination, $"api/applications/{applicationId}", new { requestedAmount, termMonths });

    [McpServerTool, Description("List documents that have been recorded against an application.")]
    public Task<ApiResult> GetApplicationDocuments(string applicationId) =>
        _client.GetAsync(LoanPlatformService.Origination, $"api/applications/{applicationId}/documents");

    [McpServerTool, Description(
        "Record that a document was uploaded for an application. This stores metadata only " +
        "(file name and document type) — it does not transmit file bytes.")]
    public Task<ApiResult> RecordDocumentUpload(string applicationId, string fileName, string documentType) =>
        _client.PostAsync(LoanPlatformService.Origination, $"api/applications/{applicationId}/documents", new { fileName, documentType });

    [McpServerTool, Description(
        "Run an ad-hoc credit check for an existing application. Optionally specify a bureau " +
        "('Experian' or 'TransUnion'); defaults to whichever bureau is registered first.")]
    public Task<ApiResult> RunCreditCheck(string applicationId, string? bureau = null)
    {
        var query = bureau is not null ? $"?bureau={Uri.EscapeDataString(bureau)}" : "";
        return _client.PostAsync(LoanPlatformService.Origination, $"api/applications/{applicationId}/credit-check{query}");
    }

    [McpServerTool, Description(
        "Submit a brand-new auto loan application. This immediately runs a live-simulated " +
        "credit check and identity check and sets the application's status to " +
        "UnderwritingInProgress, but does NOT automatically advance it any further — this " +
        "platform has no automatic event-driven processing. Call evaluate_application " +
        "afterward, using the credit score and other figures returned here, to actually run " +
        "underwriting. Returns the new ApplicationId along with the credit and identity check " +
        "results.")]
    public Task<ApiResult> SubmitApplication(
        string customerId,
        // Applicant
        string firstName, string lastName, string dateOfBirth, string ssnLastFour,
        string email, string phone, string addressLine1, string city, string state, string zipCode,
        // Employment
        string employerName, string jobTitle, decimal monthlyIncome, int employmentMonths,
        // Vehicle
        int vehicleYear, string vehicleMake, string vehicleModel, string vin, int mileage, string condition, decimal salePrice,
        // Loan terms
        decimal requestedAmount, decimal downPayment, int termMonths, string channel, string? dealerName = null)
    {
        var body = new
        {
            customerId,
            applicant = new
            {
                firstName,
                lastName,
                dateOfBirth, // expects "YYYY-MM-DD" — binds to a DateOnly server-side
                ssnLastFour,
                email,
                phone,
                addressLine1,
                city,
                state,
                zipCode,
            },
            employment = new { employerName, jobTitle, monthlyIncome, employmentMonths },
            vehicle = new
            {
                year = vehicleYear,
                make = vehicleMake,
                model = vehicleModel,
                vin,
                mileage,
                condition,
                salePrice,
            },
            requestedAmount,
            downPayment,
            termMonths,
            channel,
            dealerName,
        };

        return _client.PostAsync(LoanPlatformService.Origination, "api/applications", body);
    }
}
