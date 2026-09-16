using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoanPlatform.TestSupport;

/// <summary>
/// Stands in for real Microsoft Entra ID bearer-token auth in integration
/// tests. Every request is treated as already authenticated as a fake
/// "integration-test-user" — these tests are about verifying each
/// service's own business logic (controllers, event handlers,
/// repositories) wired together in-process, not about re-testing Entra
/// ID's token validation, which Microsoft.Identity.Web already owns and
/// which needs a real tenant to test against anyway. See
/// TestWebApplicationFactory, which registers this as the default auth
/// scheme in place of the real one.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new Claim(ClaimTypes.Name, "integration-test-user") };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
