using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Logging;
using LoanPlatform.Contracts.Events;
using UnderwritingService.Repositories;

namespace UnderwritingService;

/// <summary>
/// The core of this service: reacts to an application becoming ready for
/// underwriting, runs the risk engine (real DTI/LTV math, not just a
/// credit score threshold), records the decision, and publishes
/// UnderwritingDecisionEvent. This service never calls Origination or
/// Funding directly — it only knows about events.
/// </summary>
public class ApplicationReadyEventHandler : IEventHandler<ApplicationReadyForUnderwritingEvent>
{
    private readonly IDecisionRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly IEmailService _emailService;
    private readonly ILogger<ApplicationReadyEventHandler> _logger;

    public ApplicationReadyEventHandler(IDecisionRepository repository, IEventBus eventBus, IEmailService emailService, ILogger<ApplicationReadyEventHandler> logger)
    {
        _repository = repository;
        _eventBus = eventBus;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task HandleAsync(ApplicationReadyForUnderwritingEvent @event)
    {
        _logger.Trace(@event.ApplicationId, "ApplicationReadyEventHandler.Received", "Received ApplicationReadyForUnderwritingEvent", new { @event.EventId, @event.RequestedAmount, @event.TermMonths });

        _logger.LogInformation(
            "Evaluating application {ApplicationId} for {ApplicantName}: CreditScore={CreditScore} ({Bureau}), MonthlyIncome=${MonthlyIncome}, ExistingDebt=${ExistingDebt}, VehicleValue=${VehicleValue}",
            @event.ApplicationId, @event.ApplicantName, @event.CreditScore, @event.CreditBureauUsed, @event.MonthlyIncome, @event.ExistingMonthlyDebt, @event.VehicleValue);

        _logger.Trace(@event.ApplicationId, "ApplicationReadyEventHandler.RiskEngine.Start", "Calling RiskEngine.Evaluate", new { @event.CreditScore, @event.MonthlyIncome, @event.ExistingMonthlyDebt, @event.VehicleValue, @event.IdentityConfirmed });
        var decision = RiskEngine.Evaluate(
            @event.CreditScore, @event.IdentityConfirmed, @event.RequestedAmount,
            @event.MonthlyIncome, @event.ExistingMonthlyDebt, @event.VehicleValue);
        _logger.Trace(@event.ApplicationId, "ApplicationReadyEventHandler.RiskEngine.Done", "RiskEngine.Evaluate returned", new { decision.Approved, decision.DebtToIncomeRatio, decision.LoanToValueRatio, decision.ApprovedAmount });

        _repository.Add(new UnderwritingRecord(
            @event.ApplicationId, decision.Approved, decision.Reason,
            decision.ApprovedAmount, decision.InterestRate, @event.CreditScore,
            decision.DebtToIncomeRatio, decision.LoanToValueRatio, decision.Stipulations, DateTimeOffset.UtcNow));

        _logger.LogInformation("Decision for {ApplicationId}: Approved={Approved}, DTI={Dti:P0}, LTV={Ltv:P0} ({Reason})",
            @event.ApplicationId, decision.Approved, decision.DebtToIncomeRatio, decision.LoanToValueRatio, decision.Reason);

        await _eventBus.PublishAsync(new UnderwritingDecisionEvent
        {
            ApplicationId = @event.ApplicationId,
            Approved = decision.Approved,
            Reason = decision.Reason,
            ApprovedAmount = decision.ApprovedAmount,
            InterestRate = decision.InterestRate,
            DebtToIncomeRatio = decision.DebtToIncomeRatio,
            LoanToValueRatio = decision.LoanToValueRatio,
            Stipulations = decision.Stipulations,
            TermMonths = @event.TermMonths,
            Channel = @event.Channel,
            DealerName = @event.DealerName,
        });
        _logger.Trace(@event.ApplicationId, "ApplicationReadyEventHandler.EventPublished", "UnderwritingDecisionEvent published");

        var outcome = decision.Approved ? "APPROVED" : "DENIED";
        var stipulationsText = decision.Stipulations.Count > 0
            ? "Stipulations:\n" + string.Join("\n", decision.Stipulations.Select(s => $"  - {s}")) + "\n"
            : "";

        await _emailService.SendAsync(
            $"Loan {outcome} — {@event.ApplicationId}",
            $"Underwriting decision has been made for application {@event.ApplicationId} ({@event.ApplicantName}).\n\n" +
            $"Outcome: {outcome}\n" +
            $"Reason: {decision.Reason}\n" +
            $"Credit score: {@event.CreditScore} (via {@event.CreditBureauUsed})\n" +
            $"Debt-to-income ratio: {decision.DebtToIncomeRatio:P0}\n" +
            $"Loan-to-value ratio: {decision.LoanToValueRatio:P0}\n" +
            (decision.Approved
                ? $"Approved amount: ${decision.ApprovedAmount:N2}\nInterest rate: {decision.InterestRate}%\n"
                : "") +
            stipulationsText
        );
    }
}

/// <summary>
/// BUG FIX: closes a real data-integrity gap found while debugging the
/// dashboard's stat tiles — an application whose Underwriting decision
/// showed "Denied" turned out to have a live, fully funded, active loan in
/// Funding/Servicing. Root cause: the manual re-evaluation endpoint
/// (UnderwritingController.Evaluate — also exposed as the MCP server's
/// evaluate_application tool) lets a caller force an outcome, and
/// SqlDecisionRepository.Add() deliberately replaces the prior decision
/// row on any re-evaluation. Neither of those checked whether Funding had
/// already acted on the decision being overwritten, so a later
/// re-evaluation could silently erase all record of the approval that had
/// already triggered a real disbursement.
///
/// This handler marks a decision "Funded" the moment Funding actually
/// disburses against it. UnderwritingController.Evaluate then refuses to
/// re-evaluate (and therefore overwrite) that decision at all — once
/// money has moved, the decision that authorized it is locked.
/// </summary>
public class LoanFundedEventHandler : IEventHandler<LoanFundedEvent>
{
    private readonly IDecisionRepository _repository;
    private readonly ILogger<LoanFundedEventHandler> _logger;

    public LoanFundedEventHandler(IDecisionRepository repository, ILogger<LoanFundedEventHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task HandleAsync(LoanFundedEvent @event)
    {
        _logger.Trace(@event.ApplicationId, "LoanFundedEventHandler.Received", "Received LoanFundedEvent", new { @event.LoanId, @event.FundedAmount });
        _repository.MarkFunded(@event.ApplicationId);
        _logger.LogInformation(
            "Application {ApplicationId} marked as funded (loan {LoanId}) — its underwriting decision is now locked against re-evaluation.",
            @event.ApplicationId, @event.LoanId);
        _logger.Trace(@event.ApplicationId, "LoanFundedEventHandler.Locked", "Decision record marked Funded — future evaluate_application calls will now be refused");
        return Task.CompletedTask;
    }
}
