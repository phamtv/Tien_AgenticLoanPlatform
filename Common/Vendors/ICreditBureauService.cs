using LoanPlatform.Contracts.Models;

namespace LoanPlatform.Common.Vendors;

/// <summary>
/// Every credit bureau integration implements this — the origination
/// service depends only on this interface, not on any specific bureau,
/// so adding a new bureau or swapping which one is primary doesn't touch
/// calling code.
///
/// Implementations below simulate realistic responses (with a network-call
/// delay) rather than hitting real bureau endpoints, since this is a demo
/// without real vendor API credentials. Each implementation's doc comment
/// notes what the real integration would actually require.
/// </summary>
public interface ICreditBureauService
{
    string BureauName { get; }
    Task<CreditBureauResult> PullCreditReportAsync(string customerId, string firstName, string lastName, string ssnLastFour);
}
