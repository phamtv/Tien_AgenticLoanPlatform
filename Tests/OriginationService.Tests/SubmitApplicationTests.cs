using System.Net.Http.Json;
using System.Text.Json;
using LoanPlatform.Contracts.Events;
using LoanPlatform.TestSupport;
using OriginationService.Controllers;
using Xunit;

namespace OriginationService.Tests;

public class SubmitApplicationTests : IClassFixture<TestWebApplicationFactory<Program>>
{
    private readonly TestWebApplicationFactory<Program> _factory;

    public SubmitApplicationTests(TestWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static SubmitApplicationRequest SampleRequest(string customerId) => new(
        CustomerId: customerId,
        Applicant: new ApplicantRequest("Jane", "Doe", new DateOnly(1990, 1, 1), "0000", "jane@example.com", "555-0100", "1 Main St", "Austin", "TX", "78701"),
        Employment: new EmploymentRequest("Acme Corp", "Engineer", 9000m, 36),
        Vehicle: new VehicleRequest(2023, "Honda", "Accord", "1HGCV1F34NA000001", 5000, "Used", 28000m),
        RequestedAmount: 15000m,
        DownPayment: 13000m,
        TermMonths: 48,
        Channel: "Online",
        DealerName: null);

    [Fact]
    public async Task Submit_ValidApplication_PublishesReadyForUnderwritingEventWithTheRealFigures()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/applications", SampleRequest("CUST-INTEGRATION-EVENT"));

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // The real point of this test: submitting an application must
        // publish ApplicationReadyForUnderwritingEvent with the real
        // figures Underwriting needs to compute DTI/LTV. If this event
        // silently stopped being published, or dropped a field, an
        // application would sit stuck in "UnderwritingInProgress" forever
        // with nothing surfacing the failure — worth catching here, not
        // in production.
        //
        // Filtered by CustomerId: the TestWebApplicationFactory (and its
        // RecordingEventBus) is shared across every [Fact] in this class
        // via IClassFixture — xUnit does not reset it between test
        // methods, and does not guarantee they run in declaration order.
        // Submit_ValidApplication_ImmediatelySetsStatusToUnderwritingInProgress
        // also publishes one of these events, so an unfiltered
        // Assert.Single here is order-dependent: it only happens to pass
        // when this test runs before that one. Filtering to the specific
        // customer this test submitted makes it correct regardless of
        // run order or parallel test execution.
        var published = Assert.Single(_factory.EventBus.OfType<ApplicationReadyForUnderwritingEvent>(),
            e => e.CustomerId == "CUST-INTEGRATION-EVENT");
        Assert.Equal("CUST-INTEGRATION-EVENT", published.CustomerId);
        Assert.Equal(15000m, published.RequestedAmount);
        Assert.Equal(9000m, published.MonthlyIncome);
        Assert.Equal(28000m, published.VehicleValue);
    }

    [Fact]
    public async Task Submit_ValidApplication_ImmediatelySetsStatusToUnderwritingInProgress()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/applications", SampleRequest("CUST-INTEGRATION-STATUS"));
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var applicationId = created.GetProperty("applicationId").GetString();
        Assert.False(string.IsNullOrEmpty(applicationId));

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/applications/{applicationId}/status");
        Assert.Equal("UnderwritingInProgress", status.GetProperty("status").GetString());
    }
}
