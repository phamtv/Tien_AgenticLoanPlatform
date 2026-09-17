#if USE_SQL_SERVER
using Microsoft.EntityFrameworkCore;
#endif
using LoanPlatform.Common.AI;
using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Auth;
using LoanPlatform.Common.KeyVault;
using LoanPlatform.Common.Logging;
using LoanPlatform.Contracts.Events;
using UnderwritingService;
using UnderwritingService.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddLoanPlatformLogging();
TraceBuffer.ServiceName = "Underwriting"; // tags every TraceLogger.Trace() call from this process for the UI's Logs tab
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
builder.Services.AddDbContext<UnderwritingService.Data.UnderwritingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("UnderwritingDb")));
builder.Services.AddScoped<IDecisionRepository, SqlDecisionRepository>();
#else
builder.Services.AddSingleton<IDecisionRepository, InMemoryDecisionRepository>();
#endif
builder.Services.AddSingleton<IKeyVaultService, EnvKeyVaultService>();

// --- Underwriter co-pilot (Common/AI/ClaudeUnderwritingCopilotService.cs) ---
// Same "reads config at call time, degrades gracefully if the key is
// missing" treatment as OriginationService's document-extraction feature —
// this is an optional add-on, not core to the underwriting pipeline, so a
// missing Anthropic:ApiKey shouldn't block the service from starting.
builder.Services.AddHttpClient<IClaudeUnderwritingCopilotService, ClaudeUnderwritingCopilotService>();
// OriginationApiClient: the one synchronous cross-service call this
// service makes — see its own remarks for why the co-pilot is the
// exception to "this service never calls Origination or Funding
// directly." Services:Origination (env var Services__Origination) points
// at Origination's container DNS name in docker-compose.yml, same naming
// convention as the MCP server's ServiceEndpoints.
builder.Services.AddHttpClient<IOriginationApiClient, OriginationApiClient>();

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
    builder.Services.AddHostedService<ServiceBusEventReceiver>();
}
else
{
    builder.Services.AddHttpClient<IEventBus, HttpLoopbackEventBus>();
}
builder.Services.AddSingleton<EventDispatcher>(sp =>
{
    var dispatcher = new EventDispatcher(sp, sp.GetRequiredService<ILogger<EventDispatcher>>());
    dispatcher.RegisterEventType<ApplicationReadyForUnderwritingEvent>();
    // BUG FIX: Underwriting now also listens for LoanFundedEvent (published
    // by Funding once a loan is actually disbursed) so it can lock the
    // corresponding decision against re-evaluation — see LoanFundedEventHandler
    // in EventHandlers.cs and the Conflict check in UnderwritingController.Evaluate.
    dispatcher.RegisterEventType<LoanFundedEvent>();
    return dispatcher;
});
builder.Services.AddScoped<IEventHandler<ApplicationReadyForUnderwritingEvent>, ApplicationReadyEventHandler>();
builder.Services.AddScoped<IEventHandler<LoanFundedEvent>, LoanFundedEventHandler>();

var app = builder.Build();

#if USE_SQL_SERVER
using (var migrationScope = app.Services.CreateScope())
{
    var db = migrationScope.ServiceProvider.GetRequiredService<UnderwritingService.Data.UnderwritingDbContext>();
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
