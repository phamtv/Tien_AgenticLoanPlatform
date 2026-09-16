using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Vendors;
using LoanPlatform.Contracts.Events;
using ServicingService.Repositories;

namespace ServicingService;

/// <summary>
/// The end of the chain — reacts to a loan being funded by creating the
/// loan record payments will be tracked against. Nothing publishes an
/// event in response to this one; the loan's lifecycle from here on is
/// driven by direct API calls (recording payments), not further
/// cross-service events, since nothing else in this demo needs to react
/// to servicing activity. A real platform might still publish something
/// like LoanServicingActivatedEvent for, say, a notifications service to
/// consume — omitted here to keep the demo's event graph readable.
/// </summary>
public class LoanFundedEventHandler : IEventHandler<LoanFundedEvent>
{
    private readonly ILoanRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<LoanFundedEventHandler> _logger;

    public LoanFundedEventHandler(ILoanRepository repository, IEmailService emailService, ILogger<LoanFundedEventHandler> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task HandleAsync(LoanFundedEvent @event)
    {
        // A real servicing system assigns a customer-facing account
        // number distinct from the internal LoanId — deterministic here
        // (derived from the loan ID) so it's stable across a demo run.
        var accountNumber = $"{StableHash.Compute(@event.LoanId) % 1_000_000_000:D10}";

        var loan = _repository.AddLoan(new Repositories.Loan(
            LoanId: @event.LoanId,
            AccountNumber: accountNumber,
            ApplicationId: @event.ApplicationId,
            PrincipalAmount: @event.FundedAmount,
            InterestRate: @event.InterestRate,
            TermMonths: @event.TermMonths,
            CurrentBalance: @event.FundedAmount,
            PaymentMethod: "AutoPayACH", // default enrollment — a real system would let the borrower choose during funding
            DueDayOfMonth: 1 + (StableHash.Compute(@event.LoanId) % 28), // realistic spread of billing cycle days, avoiding 29-31 to sidestep month-length edge cases
            OriginatedAt: @event.FundedAt
        ));

        _logger.LogInformation("Loan {LoanId} (account {AccountNumber}) activated for servicing. Principal=${Principal}, Rate={Rate}%, Term={Term}mo",
            loan.LoanId, loan.AccountNumber, loan.PrincipalAmount, loan.InterestRate, loan.TermMonths);

        await _emailService.SendAsync(
            $"Loan Activated for Servicing — {loan.LoanId}",
            $"Loan {loan.LoanId} is now active and ready for payment tracking — the final stage of the origination pipeline.\n\n" +
            $"Account number: {loan.AccountNumber}\n" +
            $"Application ID: {loan.ApplicationId}\n" +
            $"Principal: ${loan.PrincipalAmount:N2}\n" +
            $"Interest rate: {loan.InterestRate}%\n" +
            $"Term: {loan.TermMonths} months\n" +
            $"Payment method: {loan.PaymentMethod}, due day {loan.DueDayOfMonth} of each month\n" +
            $"Originated at: {loan.OriginatedAt:u}\n"
        );
    }
}
