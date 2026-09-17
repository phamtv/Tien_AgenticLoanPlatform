using LoanPlatform.Common.Auth;
using LoanPlatform.Common.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServicingService.Repositories;

namespace ServicingService.Controllers;

public record MakePaymentRequest(decimal Amount);
public record UpdateLoanRequest(string Status);

[ApiController]
[Route("api/loans")]
[Authorize]
public class LoansController : ControllerBase
{
    private readonly ILoanRepository _repository;
    private readonly ILogger<LoansController> _logger;

    public LoansController(ILoanRepository repository, ILogger<LoansController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Lists all loans. No customerId filter here deliberately — Servicing
    /// doesn't own customer identity data (that's Origination's), and
    /// joining across service databases would violate the same
    /// service-owns-its-data boundary the rest of this platform follows.
    /// A real system would either denormalize a customerId onto the Loan
    /// record at funding time (adding it to LoanFundedEvent), or have the
    /// UI/BFF layer cross-reference Origination's API. This demo keeps
    /// the two concerns separate rather than fake a shortcut.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = LoanPlatformPolicies.ServicingViewLoans)]
    public IActionResult GetAll() => Ok(_repository.GetAllLoans());

    [HttpGet("{loanId}")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingViewLoans)]
    public IActionResult GetById(string loanId)
    {
        var loan = _repository.GetLoan(loanId);
        return loan is null ? NotFound() : Ok(loan);
    }

    /// <summary>
    /// Lightweight status check keyed by applicationId (not loanId — the
    /// caller doesn't necessarily know the loan's internal ID, only which
    /// application it came from, mirroring FundingsController.GetStatus).
    /// Gated by the narrower ServicingViewLoanStatus policy: returns just
    /// the loan's status (Active/Delinquent/PaidOff) and its LoanId, none
    /// of the balance, payment, or schedule detail the full endpoints
    /// expose, so roles like Loan Officer that don't own Servicing's
    /// records can still see where a loan stands.
    /// </summary>
    [HttpGet("by-application/{applicationId}/status")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingViewLoanStatus)]
    public IActionResult GetStatusByApplication(string applicationId)
    {
        var loan = _repository.GetLoanByApplicationId(applicationId);
        return loan is null
            ? Ok(new { applicationId, status = "NotStarted" })
            : Ok(new { applicationId, status = loan.Status, loan.LoanId });
    }

    /// <summary>Just the current balance — a lighter payload than the full loan record for a UI that only needs this one figure.</summary>
    [HttpGet("{loanId}/balance")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingViewLoans)]
    public IActionResult GetBalance(string loanId)
    {
        var loan = _repository.GetLoan(loanId);
        return loan is null ? NotFound() : Ok(new { loan.LoanId, loan.CurrentBalance });
    }

    /// <summary>
    /// Computes a standard amortization schedule from the loan's actual
    /// stored principal, rate, and term — real math, not a stub.
    /// </summary>
    [HttpGet("{loanId}/schedule")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingViewLoans)]
    public IActionResult GetSchedule(string loanId, [FromQuery] int? termMonths = null)
    {
        var loan = _repository.GetLoan(loanId);
        if (loan is null) return NotFound();

        // Defaults to the loan's actual stored term now that it's a real
        // field — the query override stays available for "what if this
        // had been a different term" what-if scenarios.
        var effectiveTermMonths = termMonths ?? loan.TermMonths;

        var monthlyRate = (double)loan.InterestRate / 100 / 12;
        var principal = (double)loan.PrincipalAmount;

        // Standard amortization payment formula: P * r / (1 - (1+r)^-n)
        var monthlyPayment = monthlyRate == 0
            ? principal / effectiveTermMonths
            : principal * monthlyRate / (1 - Math.Pow(1 + monthlyRate, -effectiveTermMonths));

        var schedule = new List<object>();
        var remainingBalance = principal;
        for (var month = 1; month <= effectiveTermMonths; month++)
        {
            var interestPortion = remainingBalance * monthlyRate;
            var principalPortion = monthlyPayment - interestPortion;
            remainingBalance = Math.Max(0, remainingBalance - principalPortion);

            schedule.Add(new
            {
                Month = month,
                Payment = Math.Round(monthlyPayment, 2),
                Principal = Math.Round(principalPortion, 2),
                Interest = Math.Round(interestPortion, 2),
                RemainingBalance = Math.Round(remainingBalance, 2),
            });
        }

        return Ok(new { loan.LoanId, termMonths = effectiveTermMonths, monthlyPayment = Math.Round(monthlyPayment, 2), schedule });
    }

    [HttpGet("{loanId}/payments")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingViewLoans)]
    public IActionResult GetPayments(string loanId) => Ok(_repository.GetPaymentsForLoan(loanId));

    [HttpPost("{loanId}/payments")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingPostPayments)]
    public IActionResult MakePayment(string loanId, [FromBody] MakePaymentRequest request)
    {
        var loan = _repository.GetLoan(loanId);
        if (loan is null) return NotFound(new { error = $"No loan found with id {loanId}" });

        _logger.Trace(loan.ApplicationId, "MakePayment.Start", "Payment received", new { loanId, request.Amount });

        var payment = _repository.AddPayment(new PaymentRecord(
            PaymentId: $"PMT-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            LoanId: loanId,
            Amount: request.Amount,
            PaidAt: DateTimeOffset.UtcNow
        ));

        var updatedLoan = _repository.ApplyPayment(loanId, request.Amount);

        // Auto-close the loan once the balance reaches zero.
        if (updatedLoan is not null && updatedLoan.CurrentBalance == 0 && updatedLoan.Status == "Active")
        {
            updatedLoan = _repository.UpdateStatus(loanId, "PaidOff");
            _logger.Trace(loan.ApplicationId, "MakePayment.AutoClosed", "Balance reached zero — status set to PaidOff", new { loanId });
        }

        _logger.Trace(loan.ApplicationId, "MakePayment.Done", "Payment applied", new { loanId, remainingBalance = updatedLoan?.CurrentBalance });

        return Ok(new { payment, loan = updatedLoan });
    }

    /// <summary>
    /// Updates loan status (e.g. flagging Delinquent, or a manual
    /// correction). Deliberately limited to status only — principal,
    /// rate, and origination date are set once at funding time and never
    /// edited afterward, since they came from an immutable event
    /// (LoanFundedEvent); changing them here would desync this service's
    /// record from the event that created it.
    /// </summary>
    [HttpPut("{loanId}")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingUpdateStatus)]
    public IActionResult UpdateLoan(string loanId, [FromBody] UpdateLoanRequest request)
    {
        var validStatuses = new[] { "Active", "Delinquent", "PaidOff" };
        if (!validStatuses.Contains(request.Status))
            return BadRequest(new { error = $"Status must be one of: {string.Join(", ", validStatuses)}" });

        var before = _repository.GetLoan(loanId);
        var updated = _repository.UpdateStatus(loanId, request.Status);
        if (updated is not null)
            _logger.Trace(updated.ApplicationId, "UpdateLoan.StatusChanged", "Loan status updated", new { loanId, from = before?.Status, to = request.Status });
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Generates a simple statement summary — total paid to date, current balance, and a next-payment estimate.</summary>
    [HttpPost("{loanId}/statements")]
    [Authorize(Policy = LoanPlatformPolicies.ServicingGenerateStatements)]
    public IActionResult GenerateStatement(string loanId)
    {
        var loan = _repository.GetLoan(loanId);
        if (loan is null) return NotFound();

        var payments = _repository.GetPaymentsForLoan(loanId);
        var totalPaid = payments.Sum(p => p.Amount);
        var monthlyRate = loan.InterestRate / 100 / 12;
        var estimatedNextPayment = loan.CurrentBalance > 0
            ? Math.Round(loan.CurrentBalance * (monthlyRate + 0.02m), 2) // rough estimate, not a full amortization lookup
            : 0m;

        return Ok(new
        {
            loan.LoanId,
            StatementDate = DateTimeOffset.UtcNow,
            loan.PrincipalAmount,
            loan.CurrentBalance,
            loan.Status,
            TotalPaidToDate = totalPaid,
            PaymentCount = payments.Count,
            EstimatedNextPayment = estimatedNextPayment,
        });
    }
}
