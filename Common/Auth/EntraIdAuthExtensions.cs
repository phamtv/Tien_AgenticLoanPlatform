using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

namespace LoanPlatform.Common.Auth;

/// <summary>
/// Wires up real Microsoft Entra ID bearer token validation, replacing
/// the hand-rolled JwtService/InMemoryUserStore/AuthController this
/// project used before real employee accounts and a real app
/// registration existed (see this project's git history — that version
/// worked and was fully verified, but was always a stand-in for this).
///
/// This validates tokens ONLY — it does not issue them or host a login
/// page. Entra ID is the actual token issuer now. Callers get a token
/// directly from Entra ID (for now, the MCP server via client-credentials
/// flow — see McpServer's AuthTools.cs) and pass it as
/// "Authorization: Bearer &lt;token&gt;", exactly like before; the
/// difference is entirely on the validation side, not the caller's
/// experience.
///
/// Reads the "AzureAd" config section from appsettings.json:
///   "AzureAd": {
///     "Instance": "https://login.microsoftonline.com/",
///     "TenantId": "&lt;your tenant GUID&gt;",
///     "ClientId": "&lt;this app's client GUID&gt;"
///   }
/// TenantId and ClientId are identifiers, not secrets — safe to commit,
/// same as they were safe to paste in plain chat when first generated.
/// </summary>
public static class EntraIdAuthExtensions
{
    public static WebApplicationBuilder AddLoanPlatformEntraIdAuth(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

        builder.Services.AddAuthorization();

        return builder;
    }
}
