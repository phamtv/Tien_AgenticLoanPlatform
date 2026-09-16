using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LoanPlatform.Contracts.Events;
using LoanPlatform.TestSupport;
using UnderwritingService.Controllers;
using Xunit;

namespace UnderwritingService.Tests;

public class EvaluateApplicationTests : IClassFixture<TestWebApplicationFactory<Program>>
{
    private readonly TestWebApplicationFactory<Program> _factory;

    public EvaluateApplicationTests(TestWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static ManualEvaluateRequest StrongApplicant(string customerId) => new(
        CustomerId: customerId,
        RequestedAmount: 15000m,
        CreditScore: 750,
        IdentityConfirmed: true,
        MonthlyIncome: 9000m,
        ExistingMonthlyDebt: 100m,
        VehicleValue: 28000m);

    [Fact]
    public async Task Evaluate_StrongApplicant_IsApprovedAndPublishesApprovedDecision()
    {
        var client = _factory.CreateClient();
        const string applicationId = "APP-TEST-APPROVE-1";

        var response = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", StrongApplicant("CUST-A"));
        response.EnsureSuccessStatusCode();

        var decision = await client.GetFromJsonAsync<JsonElement>($"/api/underwriting/{applicationId}/decision");
        Assert.True(decision.GetProperty("approved").GetBoolean());

        // Filtered by applicationId: TestWebApplicationFactory (and its
        // RecordingEventBus) is shared across every [Fact] in this class
        // via IClassFixture, and xUnit does not guarantee these run in
        // declaration order. Evaluate_UnconfirmedIdentity_IsDenied also
        // publishes an UnderwritingDecisionEvent, so an unfiltered
        // Assert.Single here is order-dependent — it fails whenever that
        // test happens to run first. See the same fix already applied to
        // the funded-lock regression test below.
        var published = Assert.Single(_factory.EventBus.OfType<UnderwritingDecisionEvent>(),
            e => e.ApplicationId == applicationId);
        Assert.True(published.Approved);
        Assert.Equal(applicationId, published.ApplicationId);
    }

    [Fact]
    public async Task Evaluate_UnconfirmedIdentity_IsDenied()
    {
        var client = _factory.CreateClient();
        const string applicationId = "APP-TEST-DENY-1";
        var request = StrongApplicant("CUST-B") with { IdentityConfirmed = false };

        var response = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", request);
        response.EnsureSuccessStatusCode();

        var decision = await client.GetFromJsonAsync<JsonElement>($"/api/underwriting/{applicationId}/decision");
        Assert.False(decision.GetProperty("approved").GetBoolean());
        Assert.Equal("Identity could not be confirmed.", decision.GetProperty("reason").GetString());
    }

    /// <summary>
    /// Regression test for the real bug found and fixed on 2026-09-14: an
    /// application that had already been funded (Funding acted on an
    /// earlier Approved decision) could still be silently re-evaluated to
    /// Denied, erasing all trace that a real loan had been disbursed
    /// against the original approval — see the write-up in
    /// EventHandlers.cs (LoanFundedEventHandler) and the Conflict check in
    /// UnderwritingController.Evaluate. This test reproduces the exact
    /// sequence that used to cause it and asserts it's now blocked.
    /// </summary>
    [Fact]
    public async Task Evaluate_OnAnApplicationAlreadyFunded_IsRejectedAndTheOriginalDecisionSurvives()
    {
        var client = _factory.CreateClient();
        const string applicationId = "APP-TEST-FUNDED-LOCK-1";

        // 1. Get a real Approved decision on the books, the normal way.
        var approveResponse = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", StrongApplicant("CUST-C"));
        approveResponse.EnsureSuccessStatusCode();

        // 2. Simulate Funding having actually disbursed against it, the
        // same way it happens for real — by delivering LoanFundedEvent to
        // this service's own /events/receive endpoint, exactly like the
        // real HttpLoopbackEventBus would. LoanFundedEventHandler reacts
        // to this by marking the decision funded.
        var fundedEnvelope = new
        {
            EventType = "LoanFundedEvent",
            Payload = new
            {
                ApplicationId = applicationId,
                LoanId = "LOAN-TEST-1",
                FundedAmount = 15000m,
                InterestRate = 5.9m,
                TermMonths = 60,
                DisbursementMethod = "ACH",
                FundedAt = DateTimeOffset.UtcNow,
            },
        };
        var fundedResponse = await client.PostAsJsonAsync("/events/receive", fundedEnvelope);
        fundedResponse.EnsureSuccessStatusCode();

        // 3. Now try to re-evaluate — with inputs that WOULD flip it to
        // Denied if the guard were missing. This must be rejected, and the
        // original approved decision must survive completely untouched.
        var secondAttempt = await client.PostAsJsonAsync(
            $"/api/underwriting/{applicationId}/evaluate",
            StrongApplicant("CUST-C") with { IdentityConfirmed = false });

        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);

        var decisionAfter = await client.GetFromJsonAsync<JsonElement>($"/api/underwriting/{applicationId}/decision");
        Assert.True(decisionAfter.GetProperty("approved").GetBoolean(),
            "The original approved decision must survive a blocked re-evaluation attempt — this is the exact bug this test guards against.");

        // Only one UnderwritingDecisionEvent should ever have gone out for
        // this application — the blocked attempt must not publish a
        // second, contradictory one.
        var publishedForThisApp = _factory.EventBus.OfType<UnderwritingDecisionEvent>()
            .Where(e => e.ApplicationId == applicationId)
            .ToList();
        Assert.Single(publishedForThisApp);
    }
}
