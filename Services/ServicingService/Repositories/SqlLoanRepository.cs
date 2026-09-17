using ServicingService.Data;

namespace ServicingService.Repositories;

public class SqlLoanRepository : ILoanRepository
{
    private readonly ServicingDbContext _db;

    public SqlLoanRepository(ServicingDbContext db)
    {
        _db = db;
    }

    public Loan AddLoan(Loan loan)
    {
        _db.Loans.Add(new LoanEntity
        {
            LoanId = loan.LoanId,
            AccountNumber = loan.AccountNumber,
            ApplicationId = loan.ApplicationId,
            PrincipalAmount = loan.PrincipalAmount,
            InterestRate = loan.InterestRate,
            TermMonths = loan.TermMonths,
            CurrentBalance = loan.CurrentBalance,
            PaymentMethod = loan.PaymentMethod,
            DueDayOfMonth = loan.DueDayOfMonth,
            OriginatedAt = loan.OriginatedAt.UtcDateTime,
            Status = loan.Status,
        });
        _db.SaveChanges();
        return loan;
    }

    public Loan? GetLoan(string loanId)
    {
        var entity = _db.Loans.Find(loanId);
        return entity is null ? null : ToModel(entity);
    }

    public Loan? GetLoanByApplicationId(string applicationId)
    {
        var entity = _db.Loans.FirstOrDefault(l => l.ApplicationId == applicationId);
        return entity is null ? null : ToModel(entity);
    }

    public IReadOnlyList<Loan> GetAllLoans() => _db.Loans.Select(l => ToModel(l)).ToList();

    public PaymentRecord AddPayment(PaymentRecord payment)
    {
        _db.Payments.Add(new PaymentEntity
        {
            PaymentId = payment.PaymentId,
            LoanId = payment.LoanId,
            Amount = payment.Amount,
            PaidAt = payment.PaidAt.UtcDateTime,
        });
        _db.SaveChanges();
        return payment;
    }

    public IReadOnlyList<PaymentRecord> GetPaymentsForLoan(string loanId) =>
        _db.Payments
            .Where(p => p.LoanId == loanId)
            .Select(p => new PaymentRecord(p.PaymentId, p.LoanId, p.Amount, p.PaidAt))
            .ToList();

    public Loan? ApplyPayment(string loanId, decimal amount)
    {
        var entity = _db.Loans.Find(loanId);
        if (entity is null) return null;
        entity.CurrentBalance = Math.Max(0, entity.CurrentBalance - amount);
        _db.SaveChanges();
        return ToModel(entity);
    }

    public Loan? UpdateStatus(string loanId, string status)
    {
        var entity = _db.Loans.Find(loanId);
        if (entity is null) return null;
        entity.Status = status;
        _db.SaveChanges();
        return ToModel(entity);
    }

    private static Loan ToModel(LoanEntity e) => new(
        e.LoanId, e.AccountNumber, e.ApplicationId, e.PrincipalAmount, e.InterestRate,
        e.TermMonths, e.CurrentBalance, e.PaymentMethod, e.DueDayOfMonth, e.OriginatedAt, e.Status);
}
