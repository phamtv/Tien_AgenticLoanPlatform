using System.Collections.Concurrent;

namespace ServicingService.Repositories;

public record Loan(
    string LoanId,
    string AccountNumber, // distinct from LoanId — a real servicing system uses a separate customer-facing account number, not the internal loan record ID
    string ApplicationId,
    decimal PrincipalAmount,
    decimal InterestRate,
    int TermMonths,
    decimal CurrentBalance,
    string PaymentMethod, // "AutoPayACH" | "Manual"
    int DueDayOfMonth,
    DateTimeOffset OriginatedAt,
    string Status = "Active"
);
public record PaymentRecord(string PaymentId, string LoanId, decimal Amount, DateTimeOffset PaidAt);

public interface ILoanRepository
{
    Loan AddLoan(Loan loan);
    Loan? GetLoan(string loanId);
    /// <summary>Looks a loan up by the applicationId it originated from, rather than its own LoanId — needed for the lightweight status endpoint, which is reached the same way the UI joins everything else (by applicationId).</summary>
    Loan? GetLoanByApplicationId(string applicationId);
    IReadOnlyList<Loan> GetAllLoans();
    PaymentRecord AddPayment(PaymentRecord payment);
    IReadOnlyList<PaymentRecord> GetPaymentsForLoan(string loanId);
    Loan? ApplyPayment(string loanId, decimal amount);
    Loan? UpdateStatus(string loanId, string status);
}

public class InMemoryLoanRepository : ILoanRepository
{
    private readonly ConcurrentDictionary<string, Loan> _loans = new();
    private readonly ConcurrentDictionary<string, List<PaymentRecord>> _payments = new();

    public Loan AddLoan(Loan loan)
    {
        _loans[loan.LoanId] = loan;
        _payments[loan.LoanId] = [];
        return loan;
    }

    public Loan? GetLoan(string loanId) => _loans.GetValueOrDefault(loanId);

    public Loan? GetLoanByApplicationId(string applicationId) =>
        _loans.Values.FirstOrDefault(l => l.ApplicationId == applicationId);

    public IReadOnlyList<Loan> GetAllLoans() => _loans.Values.ToList();

    public PaymentRecord AddPayment(PaymentRecord payment)
    {
        _payments.GetOrAdd(payment.LoanId, _ => []).Add(payment);
        return payment;
    }

    public IReadOnlyList<PaymentRecord> GetPaymentsForLoan(string loanId) =>
        _payments.GetValueOrDefault(loanId, []);

    public Loan? ApplyPayment(string loanId, decimal amount)
    {
        if (!_loans.TryGetValue(loanId, out var loan)) return null;
        var updated = loan with { CurrentBalance = Math.Max(0, loan.CurrentBalance - amount) };
        _loans[loanId] = updated;
        return updated;
    }

    public Loan? UpdateStatus(string loanId, string status)
    {
        if (!_loans.TryGetValue(loanId, out var loan)) return null;
        var updated = loan with { Status = status };
        _loans[loanId] = updated;
        return updated;
    }
}
