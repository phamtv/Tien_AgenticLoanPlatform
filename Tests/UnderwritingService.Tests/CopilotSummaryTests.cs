using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LoanPlatform.Common.AI;
using LoanPlatform.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using UnderwritingService;
using UnderwritingService.Controllers;
using Xunit;

namespace UnderwritingService.Tests;

/// <summary>
/// Fakes for the two things the real endpoint reaches out to, so these
/// tests exercise the real controller/repository wiring without ever
/// touching a network — same principle as RecordingEventBus standing in
/// for the real event bus. Deliberately NOT touching TestWebApplicationFactory
/// itself (shared across every test class via IClassFixture): these are
/// swapped in per-test-class via WithWebHostBuilder, so nothing here
/// affects EvaluateApplicationTests.
/// </summary>
public class FakeOriginationApiClient : IOriginationApiClient
{
    public string? ApplicationJsonToReturn { get; set; } = """{"applicationId":"APP-TEST-COPILOT-1","applicant":{"firstName":"Test","lastName":"Applicant"},"employment":{"employerName":"Self-Employed","jobTitle":"Rideshare Driver","monthlyIncome":2400.0,"employmentMonths":7},"vehicle":{"year":2018,"make":"Hyundai","model":"Elantra","salePrice":15500.0},"requestedAmount":15000.0,"downPayment":500.0,"termMonths":72}""";
    public int CallCount { get; private set; }

    public Task<string?> GetApplicationJsonAsync(string applicationId, string? bearerHeader)
    {
        CallCount++;
        return Task.FromResult(ApplicationJsonToReturn);
    }
}

public class FakeClaudeUnderwritingCopilotService : IClaudeUnderwritingCopilotService
{
    public UnderwritingCopilotResult ResultToReturn { get; set; } = new(
        true,
        JsonDocument.Parse("""{"summary":"Test summary.","riskFactors":["Self-employed, 7 months on the job"],"inconsistencies":[],"suggestedStipulations":["Proof of income required"]}""").RootElement,
        null);
    public int CallCount { get; private set; }

    public Task<UnderwritingCopilotResult> GenerateSummaryAsync(string applicationId, string applicationJson, string decisionJson)
    {
        CallCount++;
        return Task.FromResult(ResultToReturn);
    }
}

public class CopilotSummaryTests : IClassFixture<TestWebApplicationFactory<Program>>
{
    private readonly TestWebApplicationFactory<Program> _factory;

    public CopilotSummaryTests(TestWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient client, FakeOriginationApiClient origination, FakeClaudeUnderwritingCopilotService copilot) CreateClientWithFakes()
    {
        var origination = new FakeOriginationApiClient();
        var copilot = new FakeClaudeUnderwritingCopilotService();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOriginationApiClient>(origination);
                services.AddSingleton<IClaudeUnderwritingCopilotService>(copilot);
            });
        }).CreateClient();

        return (client, origination, copilot);
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
    public async Task CopilotSummary_NoExistingDecision_ReturnsNotFound()
    {
        var (client, _, copilot) = CreateClientWithFakes();

        var response = await client.PostAsync("/api/underwriting/APP-DOES-NOT-EXIST/copilot-summary", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, copilot.CallCount); // never reaches Claude for an application with no decision on file
    }

    [Fact]
    public async Task CopilotSummary_FirstCall_CallsClaudeAndCachesResult()
    {
        var (client, origination, copilot) = CreateClientWithFakes();
        const string applicationId = "APP-TEST-COPILOT-FIRST";

        var evaluateResponse = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", StrongApplicant("CUST-COPILOT-1"));
        evaluateResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/underwriting/{applicationId}/copilot-summary", null);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("cached").GetBoolean());
        Assert.Equal("Test summary.", body.GetProperty("summary").GetProperty("summary").GetString());
        Assert.Equal(1, origination.CallCount);
        Assert.Equal(1, copilot.CallCount);
    }

    /// <summary>
    /// The whole point of "manual, cached" per this feature's design: a
    /// second request for the same application must NOT call Claude (or
    /// Origination) again — it returns the cached summary from the first
    /// call. This is the test that actually guards the cost concern this
    /// feature was built around.
    /// </summary>
    [Fact]
    public async Task CopilotSummary_SecondCallWithoutRegenerate_ReturnsCachedResultWithoutCallingClaudeAgain()
    {
        var (client, origination, copilot) = CreateClientWithFakes();
        const string applicationId = "APP-TEST-COPILOT-CACHE";

        var evaluateResponse = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", StrongApplicant("CUST-COPILOT-2"));
        evaluateResponse.EnsureSuccessStatusCode();

        var first = await client.PostAsync($"/api/underwriting/{applicationId}/copilot-summary", null);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsync($"/api/underwriting/{applicationId}/copilot-summary", null);
        second.EnsureSuccessStatusCode();

        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondBody.GetProperty("cached").GetBoolean());
        Assert.Equal(1, origination.CallCount); // still just the one call from the first request
        Assert.Equal(1, copilot.CallCount);      // Claude was not called a second time
    }

    [Fact]
    public async Task CopilotSummary_RegenerateTrue_CallsClaudeAgainAndOverwritesCache()
    {
        var (client, origination, copilot) = CreateClientWithFakes();
        const string applicationId = "APP-TEST-COPILOT-REGEN";

        var evaluateResponse = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", StrongApplicant("CUST-COPILOT-3"));
        evaluateResponse.EnsureSuccessStatusCode();

        var first = await client.PostAsync($"/api/underwriting/{applicationId}/copilot-summary", null);
        first.EnsureSuccessStatusCode();

        copilot.ResultToReturn = new UnderwritingCopilotResult(
            true,
            JsonDocument.Parse("""{"summary":"Updated summary.","riskFactors":[],"inconsistencies":[],"suggestedStipulations":[]}""").RootElement,
            null);

        var second = await client.PostAsync($"/api/underwriting/{applicationId}/copilot-summary?regenerate=true", null);
        second.EnsureSuccessStatusCode();

        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(secondBody.GetProperty("cached").GetBoolean());
        Assert.Equal("Updated summary.", secondBody.GetProperty("summary").GetProperty("summary").GetString());
        Assert.Equal(2, copilot.CallCount);
    }

    [Fact]
    public async Task CopilotSummary_OriginationUnreachable_ReturnsBadGatewayAndDoesNotCallClaude()
    {
        var (client, origination, copilot) = CreateClientWithFakes();
        const string applicationId = "APP-TEST-COPILOT-NOORIGINATION";
        origination.ApplicationJsonToReturn = null; // simulate Origination 404/unreachable

        var evaluateResponse = await client.PostAsJsonAsync($"/api/underwriting/{applicationId}/evaluate", StrongApplicant("CUST-COPILOT-4"));
        evaluateResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/underwriting/{applicationId}/copilot-summary", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, copilot.CallCount); // never spends an Anthropic call when the application data couldn't even be fetched
    }
}
