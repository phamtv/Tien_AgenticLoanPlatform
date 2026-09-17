#if USE_SQL_SERVER
using Microsoft.EntityFrameworkCore;
#endif
using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Auth;
using LoanPlatform.Common.KeyVault;
using LoanPlatform.Common.Logging;
using LoanPlatform.Contracts.Events;
using FundingService;
using FundingService.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddLoanPlatformLogging();
TraceBuffer.ServiceName = "Funding"; // tags every TraceLogger.Trace() call from this process for the UI's Logs tab
builder.Services.AddControllers();

// AZURE MIGRATION CHANGE: allowed origins now come from config
// (Cors:AllowedOrigins), not a hardcoded localhost URL — see
// infra/main.bicep, which sets Cors__AllowedOrigins__0 to the UI
// Container App's public FQDN. Falls back to the original localhost
// dev-server origins when nothing is configured.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsOrigins is null || corsOrigins.Length == 0)
    corsOrigins = ["http://localhost:4200", "http://127.0.0.1:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowUI", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

#if USE_SQL_SERVER
builder.Services.AddDbContext<FundingService.Data.FundingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FundingDb")));
builder.Services.AddScoped<IFundingRepository, SqlFundingRepository>();
#else
builder.Services.AddSingleton<IFundingRepository, InMemoryFundingRepository>();
#endif
builder.Services.AddSingleton<IKeyVaultService, EnvKeyVaultService>();

// --- Auth (Microsoft Entra ID) ---
builder.AddLoanPlatformEntraIdAuth();
builder.Services.AddLoanPlatformAuthorizationPolicies();
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();

// --- Event bus ---
// EventBus:UseServiceBus (appsettings.json, overridden by docker-compose.yml)
// switches between the two IEventBus implementations with no other code
// change — see HttpLoopbackEventBus.cs vs AzureServiceBusEventBus.cs. Defaults
// to false so bare `dotnet run` behavior is unchanged; docker-compose.yml
// turns it on against the Service Bus emulator (set USE_SERVICE_BUS=false
// there to fall back instantly if the emulator itself is the problem).
if (builder.Configuration.GetValue<bool>("EventBus:UseServiceBus"))
{
    builder.Services.AddSingleton(sp => AzureServiceBusEventBus.BuildClient(sp.GetRequiredService<IConfiguration>()));
    builder.Services.AddSingleton<IEventBus, AzureServiceBusEventBus>();
    // Kept running deliberately (see AGENTIC MIGRATION note below) — with
    // the emulator, this service's subscription still receives every
    // UnderwritingDecisionEvent Origination publishes (for its own
    // status-sync bookkeeping), and something has to keep pulling and
    // completing those messages or they'd sit unacknowledged in the
    // subscription indefinitely rather than just being harmlessly ignored.
    builder.Services.AddHostedService<ServiceBusEventReceiver>();
}
else
{
    builder.Services.AddHttpClient<IEventBus, HttpLoopbackEventBus>();
}
builder.Services.AddSingleton<EventDispatcher>(sp =>
{
    var dispatcher = new EventDispatcher(sp, sp.GetRequiredService<ILogger<EventDispatcher>>());
    dispatcher.RegisterEventType<UnderwritingDecisionEvent>();
    return dispatcher;
});
// AGENTIC MIGRATION (removed auto-processing event): this used to also
// register IEventHandler<UnderwritingDecisionEvent> here, which made an
// approval auto-disburse the instant Underwriting's decision event arrived
// — no external decision in between. That's exactly the auto-advance being
// replaced by an orchestrator, so the handler registration is removed.
// UnderwritingDecisionEvent is still received (dispatcher above still
// recognizes the type, and the receiver above still runs) so messages get
// acknowledged normally — EventDispatcher.DispatchAsync just logs "no
// IEventHandler wired up" and returns, taking no action. Disbursement now
// only happens via this service's own POST /api/fundings (or
// .../{id}/disburse) — the same endpoint FundingsController.ManualFund
// already exposed and the MCP server's fund_loan/disburse_loan tools
// already call, invoked explicitly by the orchestrator or a human.
//
// UnderwritingDecisionEventHandler (EventHandlers.cs) is left in place but
// is now unreachable dead code, since it's no longer registered with the
// container — safe to delete in a later cleanup pass.

var app = builder.Build();

#if USE_SQL_SERVER
using (var migrationScope = app.Services.CreateScope())
{
    var db = migrationScope.ServiceProvider.GetRequiredService<FundingService.Data.FundingDbContext>();
    db.Database.EnsureCreated();
}
#endif


// --- Startup: log which Entra ID tenant this service is trusting ---
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation("Auth: validating bearer tokens against Entra ID tenant {TenantId}",
        app.Configuration["AzureAd:TenantId"]);
}

app.UseCors("AllowUI");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
