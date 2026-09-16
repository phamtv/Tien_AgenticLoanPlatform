using FundingService.Repositories;
using LoanPlatform.Common.Auth;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Logging;
using LoanPlatform.Contracts.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingService.Controllers;

public record ManualFundRequest(string ApplicationId, decimal ApprovedAmount, decimal InterestRate, int TermMonths = 60, string DisbursementMethod = "ACH");
public record VerifyRequest(string ApplicationId);

[ApiController]
[Route("api/fundings")]
[Authorize]
public class FundingsController : ControllerBase
{
    private readonly IFundingRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly ILogger<FundingsController> _logger;

    public FundingsController(IFundingRepository repository, IEventBus eventBus, ILogger<FundingsController> logger)
    {
        _repository = repository;
        _eventBus = eventBus;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_repository.GetAll());

    [HttpGet("{applicationId}")]
    public IActionResult GetByApplicationId(string applicationId)
    {
        var record = _repository.GetByApplicationId(applicationId);
        return record is null
            ? NotFound(new { error = $"No funding record for application {applicationId} — it may not be approved yet." })
            : Ok(record);
    }

    /// <summary>Lightweight status check, mirroring the pattern used on the other three services.</summary>
    [HttpGet("{applicationId}/status")]
    public IActionResult GetStatus(string applicationId)
    {
        var record = _repository.GetByApplicationId(applicationId);
        return record is null
            ? Ok(new { applicationId, status = "NotFunded" })
            : Ok(new { applicationId, status = "Funded", record.LoanId });
    }

    /// <summary>
    /// Simulates a title/identity re-verification step before releasing
    /// funds — a real auto-lending funding step commonly re-checks the
    /// title and borrower identity (via a provider like TrueID)
    /// immediately before disbursement, not just once back at origination,
    /// since time has passed and the loan amount is now committed.
    /// </summary>
    [HttpPost("{applicationId}/verify")]
    public IActionResult Verify(string applicationId, [FromBody] VerifyRequest request)
    {
        _logger.LogInformation("Running pre-disbursement verification for application {ApplicationId}", applicationId);
        return Ok(new { applicationId, verified = true, verifiedAt = DateTimeOffset.UtcNow, provider = "TrueID" });
    }

    /// <summary>
    /// Manually triggers disbursement outside the normal event flow — for
    /// testing, or a scenario where funding needs to be initiated directly
    /// rather than waiting on an UnderwritingDecisionEvent (e.g. a manually
    /// approved exception case). Publishes the same LoanFundedEvent either way.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ManualFund([FromBody] ManualFundRequest request)
    {
        _logger.Trace(request.ApplicationId, "ManualFund.Start", "Manual fund_loan/disburse_loan called", new { request.ApprovedAmount, request.InterestRate, request.TermMonths, request.DisbursementMethod });

        if (_repository.GetByApplicationId(request.ApplicationId) is not null)
        {
            _logger.Trace(request.ApplicationId, "ManualFund.Refused", "Refused: application already has a funding record");
            return Conflict(new { error = $"Application {request.ApplicationId} has already been funded." });
        }

        var loanId = $"LOAN-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var record = _repository.Add(new Repositories.FundingRecord(
            request.ApplicationId, loanId, request.ApprovedAmount, request.InterestRate, request.TermMonths, request.DisbursementMethod, DateTimeOffset.UtcNow));
        _logger.Trace(request.ApplicationId, "ManualFund.RecordCreated", "Funding record created", new { loanId });

        await _eventBus.PublishAsync(new LoanFundedEvent
        {
            ApplicationId = request.ApplicationId,
            LoanId = loanId,
            FundedAmount = request.ApprovedAmount,
            InterestRate = request.InterestRate,
            TermMonths = request.TermMonths,
            DisbursementMethod = request.DisbursementMethod,
            FundedAt = record.FundedAt,
        });
        _logger.Trace(request.ApplicationId, "ManualFund.EventPublished", "LoanFundedEvent published", new { loanId });

        return CreatedAtAction(nameof(GetByApplicationId), new { applicationId = request.ApplicationId }, record);
    }

    /// <summary>Alias endpoint matching the REST convention .../disburse for an explicit action verb, delegating to the same logic as the manual POST above.</summary>
    [HttpPost("{applicationId}/disburse")]
    public Task<IActionResult> Disburse(string applicationId, [FromBody] ManualFundRequest request) =>
        ManualFund(request with { ApplicationId = applicationId });
}
