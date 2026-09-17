#if USE_SQL_SERVER
using Microsoft.EntityFrameworkCore;
#endif
using LoanPlatform.Common.AI;
using LoanPlatform.Common.Email;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Common.Auth;
using LoanPlatform.Common.KeyVault;
using LoanPlatform.Common.Logging;
using LoanPlatform.Common.Vendors;
using LoanPlatform.Contracts.Events;
using OriginationService.Controllers;
using OriginationService.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddLoanPlatformLogging();
TraceBuffer.ServiceName = "Origination"; // tags every TraceLogger.Trace() call from this process for the UI's Logs tab

builder.Services.AddControllers();

// AZURE MIGRATION CHANGE: allowed origins now come from config
// (Cors:AllowedOrigins), not a hardcoded localhost URL — the UI's real
// origin isn't known until after it's deployed (see infra/main.bicep,
// which sets Cors__AllowedOrigins__0 to the UI Container App's public
// FQDN). Falls back to the original localhost dev-server origins when
// nothing is configured, so local `docker compose up` / `dotnet run`
// behavior is unchanged.
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

// --- Domain services ---
#if USE_SQL_SERVER
// Real SQL Server persistence — see Database/01_origination_schema.sql
// for the matching schema and Database/README.md for what's actually
// been verified (the T-SQL was parsed and validated; this EF Core code
// path itself has not been compiled or run anywhere yet — see that
// README and this project's .csproj for the full story).
builder.Services.AddDbContext<OriginationService.Data.OriginationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("OriginationDb")));
builder.Services.AddScoped<IApplicationRepository, SqlApplicationRepository>();
builder.Services.AddScoped<IDocumentRepository, SqlDocumentRepository>();
#else
builder.Services.AddSingleton<IApplicationRepository, InMemoryApplicationRepository>();
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
#endif

// --- Vendor integrations: 2 credit bureaus, 3 identity providers ---
// Registered as multiple implementations of the same interface —
// ApplicationsController resolves them via IEnumerable<T> and picks the
// primary one, but every vendor is available for a future routing rule
// (e.g. "use TransUnion for customers in this state").
builder.Services.AddScoped<ICreditBureauService, ExperianCreditBureauService>();
builder.Services.AddScoped<ICreditBureauService, TransUnionCreditBureauService>();
builder.Services.AddScoped<IIdentityVerificationService, LexisNexisIdentityService>();
builder.Services.AddScoped<IIdentityVerificationService, TrueIdIdentityService>();
builder.Services.AddScoped<IIdentityVerificationService, InformedIdentityService>();

// --- Key vault ---
builder.Services.AddSingleton<IKeyVaultService, EnvKeyVaultService>();

// --- Claude document extraction (pay stubs, W-2s, bank statements, IDs) ---
// Reads Anthropic:ApiKey from configuration at call time, not startup —
// unlike JwtSecret below, a missing key here degrades this one feature
// gracefully (see ClaudeDocumentExtractionService) rather than blocking
// the whole service from starting, since it's an optional add-on, not
// core to the loan pipeline.
builder.Services.AddHttpClient<IClaudeDocumentExtractionService, ClaudeDocumentExtractionService>();

// --- Auth (Microsoft Entra ID) ---
// Validates bearer tokens issued by this platform's Entra ID tenant.
// See Common/Auth/EntraIdAuthExtensions.cs and this file's AzureAd config
// section below.
builder.AddLoanPlatformEntraIdAuth();
builder.Services.AddLoanPlatformAuthorizationPolicies();

// --- Email notifications ---
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();

// --- Event bus ---
// EventBus:UseServiceBus (appsettings.json, overridden by docker-compose.yml)
// switches between the two IEventBus implementations with no other code
// change — see HttpLoopbackEventBus.cs vs AzureServiceBusEventBus.cs for why:
// the loopback bus's synchronous, blocking "publish" is what the Logs tab
// trace for APP-154817A0 exposed (a publish that only returns after its
// entire downstream cascade has already run). Defaults to false so bare
// `dotnet run` behavior is unchanged; docker-compose.yml turns it on against
// the Service Bus emulator (set USE_SERVICE_BUS=false there to fall back
// instantly if the emulator itself is the problem).
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
    dispatcher.RegisterEventType<UnderwritingDecisionEvent>();
    return dispatcher;
});
builder.Services.AddScoped<IEventHandler<UnderwritingDecisionEvent>, UnderwritingDecisionEventHandler>();

var app = builder.Build();

#if USE_SQL_SERVER
// Applies pending EF Core migrations on startup instead of EnsureCreated().
// EnsureCreated() only creates a schema if the database doesn't exist yet
// and has no concept of "what changed since last time" — exactly the gap
// that caused tonight's real bug: adding ExtractedDataJson to the C#
// entity never touched the actual SQL Server table, since EnsureCreated()
// silently did nothing against an already-existing database. Migrate()
// tracks schema history properly (via the __EFMigrationsHistory table)
// and applies only what's actually new — the fix this repo's own README
// already flagged as the right long-term answer over EnsureCreated().
using (var migrationScope = app.Services.CreateScope())
{
    var db = migrationScope.ServiceProvider.GetRequiredService<OriginationService.Data.OriginationDbContext>();
    db.Database.Migrate();
}
#endif

// --- Startup: load non-auth secrets from the key vault before accepting traffic ---
// (JwtSecret/DemoUsername/DemoPassword are gone — Entra ID now owns
// identity and token signing entirely. IKeyVaultService is kept
// registered in case a future secret needs it, e.g. the Anthropic API
// key eventually moving off plain appsettings.)
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
