using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Logging;
using LoanPlatform.Contracts.Events;
using FundingService.Repositories;

namespace FundingService;

/// <summary>
/// Reacts to an underwriting decision. Only acts if Approved — a denied
/// application is simply ignored here, since Funding has nothing to do
/// with it. This is a deliberate design point worth being able to explain:
/// the event carries enough information (Approved, ApprovedAmount,
/// InterestRate) for this service to decide independently whether it
/// needs to act, rather than Underwriting having to know which services
/// care about approvals versus denials.
/// </summary>
public class UnderwritingDecisionEventHandler : IEventHandler<UnderwritingDecisionEvent>
{
    private readonly IFundingRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly IEmailService _emailService;
    private readonly ILogger<UnderwritingDecisionEventHandler> _logger;

    public UnderwritingDecisionEventHandler(IFundingRepository repository, IEventBus eventBus, IEmailService emailService, ILogger<UnderwritingDecisionEventHandler> logger)
    {
        _repository = repository;
        _eventBus = eventBus;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task HandleAsync(UnderwritingDecisionEvent @event)
    {
        _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.Received", "Received UnderwritingDecisionEvent", new { @event.Approved, @event.ApprovedAmount, @event.EventId });

        if (!@event.Approved)
        {
            _logger.LogInformation("Application {ApplicationId} was denied — nothing for Funding to do.", @event.ApplicationId);
            _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.Skipped", "Denied — no funding action taken");
            return;
        }

        var loanId = $"LOAN-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var fundedAmount = @event.ApprovedAmount ?? 0m;
        var interestRate = @event.InterestRate ?? 0m;

        // Realistic disbursement routing: a dealer-channel application
        // typically gets wired directly to the dealer, while an online
        // application (funds going to the borrower's own account) goes
        // via ACH — a real funding service routes based on exactly this
        // kind of channel information, not a coin flip.
        var disbursementMethod = @event.Channel == "Dealer" ? "DealerWire" : "ACH";

        _logger.LogInformation("Disbursing {LoanId} for application {ApplicationId}: ${FundedAmount} at {InterestRate}% via {Method}",
            loanId, @event.ApplicationId, fundedAmount, interestRate, disbursementMethod);

        // Simulates the actual disbursement call (ACH transfer, dealer
        // payout, etc.) a real funding service would make here.
        _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.Disbursing", "Simulating disbursement call", new { disbursementMethod });
        await Task.Delay(100);

        var record = _repository.Add(new Repositories.FundingRecord(
            @event.ApplicationId, loanId, fundedAmount, interestRate, @event.TermMonths, disbursementMethod, DateTimeOffset.UtcNow));
        _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.RecordCreated", "Funding record created", new { loanId });

        await _eventBus.PublishAsync(new LoanFundedEvent
        {
            ApplicationId = @event.ApplicationId,
            LoanId = loanId,
            FundedAmount = fundedAmount,
            InterestRate = interestRate,
            TermMonths = @event.TermMonths,
            DisbursementMethod = disbursementMethod,
            FundedAt = record.FundedAt,
        });

        _logger.LogInformation("Loan {LoanId} funded and LoanFundedEvent published.", loanId);
        _logger.Trace(@event.ApplicationId, "UnderwritingDecisionEventHandler.EventPublished", "LoanFundedEvent published", new { loanId });

        await _emailService.SendAsync(
            $"Loan Funded — {loanId}",
            $"Loan {loanId} has been disbursed for application {@event.ApplicationId}.\n\n" +
            $"Funded amount: ${fundedAmount:N2}\n" +
            $"Interest rate: {interestRate}%\n" +
            $"Term: {@event.TermMonths} months\n" +
            $"Disbursement method: {disbursementMethod}" + (@event.DealerName is not null ? $" ({@event.DealerName})" : "") + "\n" +
            $"Funded at: {record.FundedAt:u}\n"
        );
    }
}
