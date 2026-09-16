namespace UnderwritingService;

/// <summary>
/// The one synchronous, cross-service call in this service — everything
/// else Underwriting does is event-driven (see EventHandlers.cs's remark
/// that "this service never calls Origination or Funding directly — it
/// only knows about events"). The co-pilot summary breaks that pattern on
/// purpose: it's a read-only, on-demand lookup triggered by a human
/// clicking a button, not part of the automatic decision pipeline, so a
/// direct HTTP call is the right shape here rather than stretching the
/// event bus to do a request/response round trip it wasn't built for.
///
/// Auth: forwards the caller's own bearer token through to Origination
/// rather than acquiring a separate service-to-service token. This works
/// because both services validate against the same Entra ID app
/// registration (compare AzureAd:ClientId/TenantId in each service's
/// appsettings.json — they're identical), so a token Underwriting already
/// validated on the incoming request is one Origination will accept too.
/// This is intentionally the exact opposite of the MCP
/// server's approach (AuthTools.Login acquires its own app-only
/// client-credentials token) — that exists because the MCP server has no
/// end-user token to forward in the first place; here, the underwriter's
/// own token already arrived on the incoming request, so passing it
/// through is both simpler and more correct: Origination sees the real
/// caller, not a shared service identity.
/// </summary>
public interface IOriginationApiClient
{
    /// <returns>Raw JSON body of the application, or null if it could not be fetched (404, network error, etc).</returns>
    Task<string?> GetApplicationJsonAsync(string applicationId, string? bearerHeader);
}

public class OriginationApiClient : IOriginationApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OriginationApiClient> _logger;

    public OriginationApiClient(HttpClient http, IConfiguration config, ILogger<OriginationApiClient> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["Services:Origination"] ?? "http://localhost:5100");
        // BUG FIX: this had no timeout at all, which meant a network problem
        // reaching Origination (wrong DNS name, a firewalled port, anything
        // that hangs rather than actively refuses) left the co-pilot button
        // stuck on "Generating…" for up to HttpClient's default 100-second
        // timeout before anything came back — and if the underlying failure
        // happened below that layer (a stalled TCP handshake some
        // environments don't fail fast on), it could sit far longer than
        // that. This is an internal, same-Docker-network call to a service
        // that should already be up (see docker-compose.yml's soft
        // depends_on for this service) — 15 seconds is generous for that,
        // and bounds the failure to something a person waiting on a click
        // will actually stick around for.
        _http.Timeout = TimeSpan.FromSeconds(15);
        _logger = logger;
    }

    public async Task<string?> GetApplicationJsonAsync(string applicationId, string? bearerHeader)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/applications/{applicationId}");
        if (!string.IsNullOrWhiteSpace(bearerHeader))
        {
            // TryAddWithoutValidation rather than setting request.Headers.Authorization
            // directly — the incoming header is already a well-formed
            // "Bearer <token>" string being passed straight through, not
            // reconstructed, so there's no scheme/parameter to validate here.
            request.Headers.TryAddWithoutValidation("Authorization", bearerHeader);
        }

        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Origination returned {StatusCode} fetching application {ApplicationId}", (int)response.StatusCode, applicationId);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException is what HttpClient actually throws on a
            // timeout (it wraps a TimeoutException) — HttpRequestException
            // alone only covers an active failure (connection refused, DNS
            // failure), not a hang, so catching just that left a timeout
            // free to propagate as an unhandled exception instead of the
            // clean "could not reach Origination" result callers expect.
            _logger.LogError(ex, "Could not reach Origination to fetch application {ApplicationId} (timed out after {Timeout})", applicationId, _http.Timeout);
            return null;
        }
    }
}
