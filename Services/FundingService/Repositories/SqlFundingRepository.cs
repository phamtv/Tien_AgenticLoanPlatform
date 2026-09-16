using FundingService.Data;

namespace FundingService.Repositories;

public class SqlFundingRepository : IFundingRepository
{
    private readonly FundingDbContext _db;

    public SqlFundingRepository(FundingDbContext db)
    {
        _db = db;
    }

    public FundingRecord Add(FundingRecord record)
    {
        _db.Fundings.Add(new FundingEntity
        {
            LoanId = record.LoanId,
            ApplicationId = record.ApplicationId,
            FundedAmount = record.FundedAmount,
            InterestRate = record.InterestRate,
            TermMonths = record.TermMonths,
            DisbursementMethod = record.DisbursementMethod,
            FundedAt = record.FundedAt.UtcDateTime,
        });
        _db.SaveChanges();
        return record;
    }

    public FundingRecord? GetByApplicationId(string applicationId)
    {
        var entity = _db.Fundings.FirstOrDefault(f => f.ApplicationId == applicationId);
        return entity is null ? null : ToModel(entity);
    }

    public IReadOnlyList<FundingRecord> GetAll() =>
        _db.Fundings.Select(f => ToModel(f)).ToList();

    private static FundingRecord ToModel(FundingEntity e) =>
        new(e.ApplicationId, e.LoanId, e.FundedAmount, e.InterestRate, e.TermMonths, e.DisbursementMethod, e.FundedAt);
}
