using LoanPlatform.Common.AI;
using LoanPlatform.Common.Auth;
using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Logging;
using LoanPlatform.Common.Vendors;
using LoanPlatform.Contracts.Events;
using LoanPlatform.Contracts.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OriginationService.Repositories;

namespace OriginationService.Controllers;

// Request DTOs mirror the domain model's nested shape (Applicant,
// Employment, Vehicle) — a real LOS intake form collects exactly these
// groups of fields, not a flat list.
public record ApplicantRequest(string FirstName, string LastName, DateOnly DateOfBirth, string SsnLastFour, string Email, string Phone, string AddressLine1, string City, string State, string ZipCode);
public record EmploymentRequest(string EmployerName, string JobTitle, decimal MonthlyIncome, int EmploymentMonths);
public record VehicleRequest(int Year, string Make, string Model, string Vin, int Mileage, string Condition, decimal SalePrice);

public record SubmitApplicationRequest(
    string CustomerId,
    ApplicantRequest Applicant,
    EmploymentRequest Employment,
    VehicleRequest Vehicle,
    decimal RequestedAmount,
    decimal DownPayment,
    int TermMonths,
    string Channel,
    string? DealerName
);

public record UpdateApplicationRequest(decimal? RequestedAmount, int? TermMonths);

// ExtractedDataJson is optional — existing callers (including the MCP
// server's RecordDocumentUpload tool) that only send FileName and
// DocumentType are unaffected. It's populated when the client submits
// the user-verified result of the /documents/extract endpoint below.
public record UploadDocumentRequest(string FileName, string DocumentType, string? ExtractedDataJson = null);

[ApiController]
[Route("api/applications")]
[Authorize]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationRepository _repository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEnumerable<ICreditBureauService> _creditBureaus;
    private readonly IEnumerable<IIdentityVerificationService> _identityProviders;
    private readonly IEventBus _eventBus;
    private readonly IEmailService _emailService;
    private readonly IClaudeDocumentExtractionService _extractionService;
    private readonly ILogger<ApplicationsController> _logger;

    public ApplicationsController(
        IApplicationRepository repository,
        IDocumentRepository documentRepository,
        IEnumerable<ICreditBureauService> creditBureaus,
        IEnumerable<IIdentityVerificationService> identityProviders,
        IEventBus eventBus,
        IEmailService emailService,
        IClaudeDocumentExtractionService extractionService,
        ILogger<ApplicationsController> logger)
    {
        _repository = repository;
        _documentRepository = documentRepository;
        _creditBureaus = creditBureaus;
        _identityProviders = identityProviders;
        _eventBus = eventBus;
        _emailService = emailService;
        _extractionService = extractionService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_repository.GetAll());

    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        var application = _repository.GetById(id);
        return application is null ? NotFound() : Ok(application);
    }

    [HttpGet("{id}/status")]
    public IActionResult GetStatus(string id)
    {
        var application = _repository.GetById(id);
        return application is null ? NotFound() : Ok(new { application.ApplicationId, application.Status });
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] UpdateApplicationRequest request)
    {
        var existing = _repository.GetById(id);
        if (existing is null) return NotFound();

        if (existing.Status is "Approved" or "Denied" or "Funded")
            return Conflict(new { error = $"Application {id} already has a status of '{existing.Status}' and can no longer be edited." });

        var updated = existing with
        {
            RequestedAmount = request.RequestedAmount ?? existing.RequestedAmount,
            TermMonths = request.TermMonths ?? existing.TermMonths,
        };
        _repository.Add(updated);
        return Ok(updated);
    }

    [HttpPost("{id}/documents")]
    public IActionResult UploadDocument(string id, [FromBody] UploadDocumentRequest request)
    {
        if (_repository.GetById(id) is null) return NotFound(new { error = $"No application found with id {id}" });

        var doc = _documentRepository.Add(new Repositories.ApplicationDocument(
            DocumentId: $"DOC-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            ApplicationId: id,
            FileName: request.FileName,
            DocumentType: request.DocumentType,
            UploadedAt: DateTimeOffset.UtcNow,
            ExtractedDataJson: request.ExtractedDataJson
        ));
        return Ok(doc);
    }

    [HttpGet("{id}/documents")]
    public IActionResult GetDocuments(string id) => Ok(_documentRepository.GetForApplication(id));

    /// <summary>
    /// Uploads a loan document (pay stub, W-2, bank statement, or ID) and
    /// sends it to Claude for structured field extraction. This is a
    /// preview step — nothing is persisted here. The caller (the Angular
    /// UI's review screen) is expected to let the user correct any field,
    /// then POST the verified result to POST /documents above to actually
    /// save it against the application.
    /// </summary>
    [HttpPost("{id}/documents/extract")]
    [RequestSizeLimit(20_000_000)] // 20MB — generous for a scanned multi-page PDF or a phone photo
    public async Task<IActionResult> ExtractDocument(string id, IFormFile file, [FromForm] string documentType)
    {
        if (_repository.GetById(id) is null) return NotFound(new { error = $"No application found with id {id}" });
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file was uploaded." });
        if (!DocumentExtractionSchemas.SupportedTypes.Contains(documentType))
            return BadRequest(new { error = $"Unknown documentType '{documentType}'. Expected one of: {string.Join(", ", DocumentExtractionSchemas.SupportedTypes)}." });

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var bytes = stream.ToArray();
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        _logger.LogInformation("Extracting {DocumentType} ({SizeBytes} bytes) for application {ApplicationId} via Claude",
            documentType, bytes.Length, id);

        var result = await _extractionService.ExtractAsync(bytes, contentType, documentType);

        if (!result.Success)
            return UnprocessableEntity(new { error = result.Error, documentType });

        return Ok(new
        {
            applicationId = id,
            fileName = file.FileName,
            documentType,
            extractedData = result.Data,
        });
    }

    [HttpPost("{id}/credit-check")]
    public async Task<IActionResult> RunCreditCheck(string id, [FromQuery] string? bureau)
    {
        var application = _repository.GetById(id);
        if (application is null) return NotFound(new { error = $"No application found with id {id}" });

        var creditBureau = bureau is not null
            ? _creditBureaus.FirstOrDefault(b => b.BureauName.Equals(bureau, StringComparison.OrdinalIgnoreCase))
            : _creditBureaus.First();

        if (creditBureau is null)
            return BadRequest(new { error = $"Unknown bureau '{bureau}'. Available: {string.Join(", ", _creditBureaus.Select(b => b.BureauName))}" });

        var result = await creditBureau.PullCreditReportAsync(
            application.Applicant.FirstName + application.CustomerId, // seed varies from the original pull, simulating a fresh report
            application.Applicant.FirstName, application.Applicant.LastName, application.Applicant.SsnLastFour);
        return Ok(result);
    }

    /// <summary>
    /// Submits a new application with a realistic LOS intake shape:
    /// applicant demographics, employment, and vehicle detail — not just
    /// a bare amount and VIN. Calls a credit bureau and an identity
    /// provider, then publishes ApplicationReadyForUnderwritingEvent with
    /// enough real financial detail (income, existing debt, vehicle value)
    /// for Underwriting to compute actual DTI and LTV ratios.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitApplicationRequest request)
    {
        var applicationId = $"APP-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var fullName = $"{request.Applicant.FirstName} {request.Applicant.LastName}";

        var creditBureau = _creditBureaus.First();
        var identityProvider = _identityProviders.First();

        _logger.Trace(applicationId, "Submit.Start", "New application submitted", new { request.CustomerId, fullName, request.RequestedAmount, request.TermMonths, request.Channel });

        _logger.LogInformation("Submitting application {ApplicationId} for {CustomerId} ({Name}), pulling credit via {Bureau} and identity via {IdentityProvider}",
            applicationId, request.CustomerId, fullName, creditBureau.BureauName, identityProvider.ProviderName);

        _logger.Trace(applicationId, "CreditCheck.Start", "Pulling credit report", new { bureau = creditBureau.BureauName });
        var creditResult = await creditBureau.PullCreditReportAsync(request.CustomerId, request.Applicant.FirstName, request.Applicant.LastName, request.Applicant.SsnLastFour);
        _logger.Trace(applicationId, "CreditCheck.Done", "Credit report received", new { creditResult.CreditScore, creditResult.RiskTier, creditResult.TotalMonthlyDebtPayments });

        _logger.Trace(applicationId, "IdentityCheck.Start", "Verifying identity", new { provider = identityProvider.ProviderName });
        var identityResult = await identityProvider.VerifyIdentityAsync(request.CustomerId, fullName);
        _logger.Trace(applicationId, "IdentityCheck.Done", "Identity check complete", new { identityResult.IdentityConfirmed });

        var applicant = new Applicant(
            request.Applicant.FirstName, request.Applicant.LastName, request.Applicant.DateOfBirth, request.Applicant.SsnLastFour,
            request.Applicant.Email, request.Applicant.Phone, request.Applicant.AddressLine1, request.Applicant.City, request.Applicant.State, request.Applicant.ZipCode);
        var employment = new Employment(request.Employment.EmployerName, request.Employment.JobTitle, request.Employment.MonthlyIncome, request.Employment.EmploymentMonths);
        var vehicle = new Vehicle(request.Vehicle.Year, request.Vehicle.Make, request.Vehicle.Model, request.Vehicle.Vin, request.Vehicle.Mileage, request.Vehicle.Condition, request.Vehicle.SalePrice);

        var application = _repository.Add(new LoanApplication(
            ApplicationId: applicationId,
            CustomerId: request.CustomerId,
            Applicant: applicant,
            Employment: employment,
            Vehicle: vehicle,
            RequestedAmount: request.RequestedAmount,
            DownPayment: request.DownPayment,
            TermMonths: request.TermMonths,
            Channel: request.Channel,
            DealerName: request.DealerName,
            Status: "Submitted",
            SubmittedAt: DateTimeOffset.UtcNow
        ));

        // AGENTIC MIGRATION (removed auto-processing event): this used to
        // publish ApplicationReadyForUnderwritingEvent here, which
        // Underwriting's ApplicationReadyEventHandler picked up automatically
        // to run the risk engine with no external decision in between. That
        // auto-advance is exactly what's being replaced by an orchestrator —
        // this method now stops at "UnderwritingInProgress" and leaves the
        // application there. Underwriting.Evaluate (POST
        // /api/underwriting/{id}/evaluate — the same endpoint the MCP
        // server's evaluate_application tool calls, and the same RiskEngine
        // + UnderwritingDecisionEvent publish the old handler used) is now
        // the only thing that moves an application past this point, and it's
        // only ever called explicitly — by the orchestrator or a human —
        // never automatically from here.
        //
        // ApplicationReadyForUnderwritingEvent, ApplicationReadyEventHandler,
        // and their registration in UnderwritingService/Program.cs are left
        // in place but are now unreachable dead code, since nothing publishes
        // that event type anymore — safe to delete in a later cleanup pass.
        _repository.UpdateStatus(applicationId, "UnderwritingInProgress");
        _logger.Trace(applicationId, "Status.Set", "Status set to UnderwritingInProgress — awaiting an explicit call to Underwriting's evaluate endpoint (auto-publish removed)");

        await _emailService.SendAsync(
            $"Loan Application Submitted — {applicationId}",
            $"A new loan application has been submitted and is awaiting underwriting review.\n\n" +
            $"Application ID: {applicationId}\n" +
            $"Applicant: {fullName} ({request.CustomerId})\n" +
            $"Employer: {request.Employment.EmployerName}, monthly income ${request.Employment.MonthlyIncome:N2}\n" +
            $"Vehicle: {request.Vehicle.Year} {request.Vehicle.Make} {request.Vehicle.Model} — ${request.Vehicle.SalePrice:N2}\n" +
            $"Requested amount: ${request.RequestedAmount:N2} over {request.TermMonths} months\n" +
            $"Channel: {request.Channel}" + (request.DealerName is not null ? $" ({request.DealerName})" : "") + "\n\n" +
            $"Credit check: {creditResult.BureauName} ({creditResult.ScoreModel}), score {creditResult.CreditScore} ({creditResult.RiskTier}), " +
            $"{creditResult.Tradelines.Count} tradelines, ${creditResult.TotalMonthlyDebtPayments:N2}/mo existing debt\n" +
            $"Identity check: {identityResult.ProviderName}, confirmed: {identityResult.IdentityConfirmed}\n"
        );

        return CreatedAtAction(nameof(GetById), new { id = applicationId }, new
        {
            application.ApplicationId,
            application.CustomerId,
            application.Applicant,
            Status = "UnderwritingInProgress",
            CreditCheck = creditResult,
            IdentityCheck = identityResult,
        });
    }
}

/// <summary>
/// Reacts to the underwriting decision by updating this application's
/// status — Origination cares about the outcome even though Funding is
/// the one that acts on an approval.
/// </summary>
public class UnderwritingDecisionEventHandler : IEventHandler<UnderwritingDecisionEvent>
{
    private readonly IApplicationRepository _repository;
    private readonly ILogger<UnderwritingDecisionEventHandler> _logger;

    public UnderwritingDecisionEventHandler(IApplicationRepository repository, ILogger<UnderwritingDecisionEventHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task HandleAsync(UnderwritingDecisionEvent @event)
    {
        _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.Received", "Received UnderwritingDecisionEvent", new { @event.Approved, @event.EventId });
        var newStatus = @event.Approved ? "Approved" : "Denied";
        _repository.UpdateStatus(@event.ApplicationId, newStatus);
        _logger.LogInformation("Application {ApplicationId} status updated to {Status} following underwriting decision", @event.ApplicationId, newStatus);
        _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.StatusUpdated", "Status write complete", new { newStatus });
        return Task.CompletedTask;
    }
}
