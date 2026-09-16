using System.Collections.Concurrent;

namespace FundingService.Repositories;

public record FundingRecord(string ApplicationId, string LoanId, decimal FundedAmount, decimal InterestRate, int TermMonths, string DisbursementMethod, DateTimeOffset FundedAt);

public interface IFundingRepository
{
    FundingRecord Add(FundingRecord record);
    FundingRecord? GetByApplicationId(string applicationId);
    IReadOnlyList<FundingRecord> GetAll();
}

public class InMemoryFundingRepository : IFundingRepository
{
    private readonly ConcurrentDictionary<string, FundingRecord> _fundings = new();

    public FundingRecord Add(FundingRecord record)
    {
        _fundings[record.ApplicationId] = record;
        return record;
    }

    public FundingRecord? GetByApplicationId(string applicationId) =>
        _fundings.GetValueOrDefault(applicationId);

    public IReadOnlyList<FundingRecord> GetAll() => _fundings.Values.ToList();
}
