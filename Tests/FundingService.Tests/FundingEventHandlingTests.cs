using System.Net.Http.Json;
using System.Text.Json;
using LoanPlatform.Contracts.Events;
using LoanPlatform.TestSupport;
using Xunit;

namespace FundingService.Tests;

public class FundingEventHandlingTests : IClassFixture<TestWebApplicationFactory<Program>>
{
    private readonly TestWebApplicationFactory<Program> _factory;

    public FundingEventHandlingTests(TestWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static object DecisionEnvelope(string applicationId, bool approved) => new
    {
        EventType = "UnderwritingDecisionEvent",
        Payload = new
        {
            ApplicationId = applicationId,
            Approved = approved,
            Reason = approved ? "Approved based on credit score 750, DTI 10%, LTV 50%." : "Identity could not be confirmed.",
            ApprovedAmount = approved ? 15000m : (decimal?)null,
            InterestRate = approved ? 5.9m : (decimal?)null,
            DebtToIncomeRatio = 0.10m,
            LoanToValueRatio = 0.50m,
            Stipulations = Array.Empty<string>(),
            TermMonths = 60,
            Channel = "Online",
            DealerName = (string?)null,
        },
    };

    [Fact]
    public async Task ApprovedDecision_CreatesAFundingRecordAndPublishesLoanFundedEvent()
    {
        var client = _factory.CreateClient();
        const string applicationId = "APP-TEST-FUND-APPROVE-1";

        var response = await client.PostAsJsonAsync("/events/receive", DecisionEnvelope(applicationId, approved: true));
        response.EnsureSuccessStatusCode();

        var fundings = await client.GetFromJsonAsync<JsonElement>("/api/fundings");
        Assert.Contains(fundings.EnumerateArray(), f => f.GetProperty("applicationId").GetString() == applicationId);

        // Filtered by applicationId: TestWebApplicationFactory (and its
        // RecordingEventBus) is shared across every [Fact] in this class
        // via IClassFixture, and xUnit does not guarantee these run in
        // declaration order — an unfiltered Assert.Single/Assert.Empty
        // against the whole bus is order-dependent once more than one
        // test in the class can touch the same event type.
        var published = Assert.Single(_factory.EventBus.OfType<LoanFundedEvent>(),
            e => e.ApplicationId == applicationId);
        Assert.Equal(applicationId, published.ApplicationId);
        Assert.Equal(15000m, published.FundedAmount);
    }

    /// <summary>
    /// Core invariant Funding must never violate: it must not act on a
    /// denied decision. This check stayed intact throughout the real bug
    /// found on 2026-09-14 — that bug came from a *second, out-of-band*
    /// re-evaluation overwriting an already-funded decision back in
    /// Underwriting, not from Funding itself acting on a denial. This test
    /// guards the invariant directly, so a future change to
    /// UnderwritingDecisionEventHandler here can't silently break it.
    /// </summary>
    [Fact]
    public async Task DeniedDecision_CreatesNoFundingRecordAndPublishesNothing()
    {
        var client = _factory.CreateClient();
        const string applicationId = "APP-TEST-FUND-DENY-1";

        var response = await client.PostAsJsonAsync("/events/receive", DecisionEnvelope(applicationId, approved: false));
        response.EnsureSuccessStatusCode();

        var fundings = await client.GetFromJsonAsync<JsonElement>("/api/fundings");
        Assert.DoesNotContain(fundings.EnumerateArray(), f => f.GetProperty("applicationId").GetString() == applicationId);

        Assert.DoesNotContain(_factory.EventBus.OfType<LoanFundedEvent>(), e => e.ApplicationId == applicationId);
    }
}
