using System.ComponentModel;
using LoanPlatform.McpServer.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using ModelContextProtocol.Server;

namespace LoanPlatform.McpServer.Tools;

/// <summary>
/// Replaces the old username/password Login tool (which called the now-
/// retired Common/Auth/AuthController) with a real Entra ID
/// client-credentials token acquisition, using MSAL.NET
/// (Microsoft.Identity.Client) — the standard library for exactly this,
/// not hand-rolled, for the same reasoning as EntraIdAuthExtensions.cs on
/// the services side: token acquisition is security-sensitive code worth
/// using a maintained library for.
///
/// This is genuinely a scoped step, not full per-employee auth: the
/// token this acquires represents the MCP server/platform itself
/// ("app-only"), not a specific logged-in person — real per-employee
/// identity requires an interactive sign-in flow, which is the separate
/// Connectors/OAuth work planned for when this server moves to HTTP
/// transport. For now, every tool call still shares one identity, same
/// as the old shared demo account — the meaningful upgrade here is that
/// the token is now genuinely issued and verifiable by Entra ID, not a
/// locally-signed JWT trusting a shared secret.
/// </summary>
[McpServerToolType]
public class AuthTools
{
    private readonly TokenStore _tokenStore;
    private readonly IConfiguration _config;

    public AuthTools(TokenStore tokenStore, IConfiguration config)
    {
        _tokenStore = tokenStore;
        _config = config;
    }

    [McpServerTool(Name = "login"), Description(
        "Log in to the loan platform. Call this before any other tool — every business " +
        "endpoint across all four services requires a Bearer token issued by this platform's " +
        "Microsoft Entra ID tenant. Acquires an app-only token via client credentials; no " +
        "username/password needed anymore.")]
    public async Task<object> Login()
    {
        var tenantId = _config["AzureAd:TenantId"];
        var clientId = _config["AzureAd:ClientId"];
        var clientSecret = _config["AzureAd:ClientSecret"];
        var apiScope = _config["AzureAd:ApiScope"]; // e.g. "api://8af584e0-.../.default"

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(apiScope))
        {
            return new
            {
                success = false,
                message = "Missing AzureAd configuration (TenantId, ClientId, ClientSecret, ApiScope). " +
                           "Set AzureAd__ClientSecret as an environment variable — never commit it to appsettings.json."
            };
        }

        try
        {
            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            var result = await app.AcquireTokenForClient(new[] { apiScope }).ExecuteAsync();

            _tokenStore.Token = result.AccessToken;
            _tokenStore.Username = "loan-platform-mcp (app-only)";

            return new
            {
                success = true,
                message = "Authenticated with Entra ID (app-only token). Token stored for this session " +
                           "and will be attached to every call to all four services.",
                expiresOn = result.ExpiresOn
            };
        }
        catch (MsalException ex)
        {
            return new
            {
                success = false,
                message = "Entra ID token acquisition failed.",
                detail = ex.Message
            };
        }
    }

    [McpServerTool(Name = "auth_status"), Description("Check whether this MCP server currently holds a stored Entra ID token.")]
    public object AuthStatus() => new { authenticated = _tokenStore.IsAuthenticated, identity = _tokenStore.Username };
}
