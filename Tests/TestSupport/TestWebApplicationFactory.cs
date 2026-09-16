using LoanPlatform.Common.EventBus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoanPlatform.TestSupport;

/// <summary>
/// Boots one service fully in-process (real controllers, real event
/// handlers, the real RiskEngine, the real in-memory repository — the
/// default when UseSqlServer isn't set, which is how `dotnet test` builds
/// these projects) for integration testing, with exactly two things
/// swapped out:
///   - Real Entra ID bearer-token auth -> TestAuthHandler (always succeeds)
///   - Real HTTP event bus -> RecordingEventBus (records instead of
///     POSTing to another service over the network)
/// Everything else runs for real, including the service's own
/// /events/receive endpoint (EventsController, inherited from Common) —
/// so a test can simulate "another service published this event" by
/// POSTing straight to /events/receive, exactly the way the real
/// HttpLoopbackEventBus would deliver it in production.
/// </summary>
public class TestWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public RecordingEventBus EventBus { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IEventBus));
            services.AddSingleton<IEventBus>(EventBus);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
