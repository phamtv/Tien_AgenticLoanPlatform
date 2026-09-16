using System.Security.Cryptography;
using System.Text;
using LoanPlatform.McpServer.Configuration;
using LoanPlatform.McpServer.Http;
using Microsoft.Extensions.Logging.Console;

// --- Transport selection ---
// Mcp__Transport decides which *kind* of host this process becomes — a
// decision that has to happen before any builder exists, because stdio and
// HTTP need genuinely different hosts (a bare generic Host vs. a
// WebApplication with Kestrel behind it), not just a different call on a
// shared one. Read directly from the environment rather than through
// IConfiguration for that reason: there's no configuration object yet to
// read from at this point. Defaults to Stdio so `dotnet run` locally — and
// Claude Desktop launching this as a subprocess, per this project's
// README's original setup instructions — behaves exactly as it always has.
// Only docker-compose.yml's mcp-server service sets Mcp__Transport=Http.
var useHttpTransport = string.Equals(
    Environment.GetEnvironmentVariable("Mcp__Transport"),
    "Http",
    StringComparison.OrdinalIgnoreCase);

if (useHttpTransport)
{
    await RunHttpAsync(args);
}
else
{
    await RunStdioAsync(args);
}

// --- Shared DI, used by both transports ---
static void ConfigureSharedServices(IServiceCollection services, IConfiguration configuration)
{
    // Reads the "Services" section from appsettings.json, overridable via the
    // same ASP.NET Core-style Services__Origination=... env var convention
    // used throughout the rest of this repo. docker-compose.yml's mcp-server
    // service overrides these to the four services' container DNS names
    // (e.g. http://origination:8080) instead of the appsettings.json
    // localhost defaults used for local, non-containerized `dotnet run`.
    services.Configure<ServiceEndpoints>(configuration.GetSection("Services"));

    services.AddSingleton<TokenStore>();

    // Typed HttpClient — LoanPlatformApiClient's constructor takes HttpClient
    // plus IOptions<ServiceEndpoints> and TokenStore, both resolved from DI
    // automatically alongside it.
    services.AddHttpClient<LoanPlatformApiClient>();
}

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // MCP's stdio transport uses stdout as the actual JSON-RPC message
    // channel — any stray line written to stdout (which is exactly what the
    // generic host's default console logger does) corrupts the protocol
    // stream from the client's point of view. This adjusts the console
    // logger options the default host already registered, rather than
    // calling AddConsole() again (which would add a second provider and
    // double every log line), so every log level goes to stderr instead.
    // Latent bug in the original stdio-only version of this file: harmless
    // while nobody had actually run it, real the moment something logs.
    builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    ConfigureSharedServices(builder.Services, builder.Configuration);

    // WithToolsFromAssembly() scans this assembly for every [McpServerToolType]
    // class (AuthTools, OriginationTools, UnderwritingTools, FundingTools,
    // ServicingTools) and registers their [McpServerTool] methods.
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    await app.RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureSharedServices(builder.Services, builder.Configuration);

    // WithHttpTransport() + app.MapMcp() below is the streamable-HTTP
    // transport from ModelContextProtocol.AspNetCore — the package this
    // project's README previously flagged as "not included here", added now
    // for the container deployment. No explicit `using` for that package
    // below: like AddMcpServer() itself (already used in the stdio path
    // above with no extra using needed), the SDK puts its IServiceCollection/
    // IEndpointRouteBuilder extensions directly in namespaces Sdk.Web's
    // implicit usings already cover. If this doesn't resolve when you
    // actually build it, `using ModelContextProtocol.AspNetCore;` is the
    // one-line fix — same "verify before trusting" note as the rest of this
    // project.
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    // --- Access control for the HTTP endpoint ---
    // Stdio's access control was always implicit: only whatever can launch
    // this process as a subprocess can reach it (see this project's README,
    // "Auth on the MCP server itself"). HTTP removes that boundary entirely —
    // anything that can reach this container's port could call every tool
    // across all four services. This closes that gap with a shared-secret
    // header check, deliberately NOT the same Entra ID bearer validation the
    // four business services use (Common/Auth/EntraIdAuthExtensions.cs):
    // that validates tokens meant to be acquired freshly and expire in about
    // an hour, which is exactly the wrong shape for a static header value
    // sitting in an MCP client's config file with no refresh logic behind
    // it. A long-lived shared secret — the same trust model this repo
    // already uses for AZURE_AD_CLIENT_SECRET — is the honest fit for this
    // specific boundary. Real per-employee identity on this endpoint is the
    // separate interactive OAuth/Connectors work AuthTools.cs's Login already
    // flags as a later step, not this one.
    var apiKey = builder.Configuration["Mcp:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException(
            "Mcp:ApiKey (env var MCP_API_KEY) must be set before starting the MCP server in HTTP mode — " +
            "refusing to start unprotected on the network. Stdio mode (the default) doesn't need this.");
    }

    var expectedHeader = $"Bearer {apiKey}";
    app.Use(async (context, next) =>
    {
        var provided = context.Request.Headers.Authorization.ToString();
        if (!FixedTimeEquals(provided, expectedHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid MCP API key." });
            return;
        }

        await next();
    });

    app.MapMcp();

    await app.RunAsync();
}

// Constant-time comparison so a mistyped or probing key can't be narrowed
// down by measuring how long the comparison takes — cheap to do correctly
// via CryptographicOperations, not worth skipping even for a demo project.
static bool FixedTimeEquals(string a, string b)
{
    var aBytes = Encoding.UTF8.GetBytes(a);
    var bBytes = Encoding.UTF8.GetBytes(b);
    return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
}
