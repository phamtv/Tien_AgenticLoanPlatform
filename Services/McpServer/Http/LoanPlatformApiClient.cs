using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LoanPlatform.McpServer.Configuration;
using Microsoft.Extensions.Options;

namespace LoanPlatform.McpServer.Http;

public enum LoanPlatformService { Origination, Underwriting, Funding, Servicing }

/// <summary>
/// Result shape returned by every tool in this project. Deliberately
/// includes StatusCode and Success even on failure, rather than throwing
/// — a 404 ("no decision found for this application yet, it may still be
/// processing") or a 409 ("already funded") is meaningful domain
/// information for whatever's driving these tools, not an exceptional
/// condition to hide.
/// </summary>
public record ApiResult(int StatusCode, bool Success, JsonElement? Body, string? Error = null);

/// <summary>
/// Shared HTTP client for all four downstream services. Attaches the
/// Bearer token from TokenStore (once AuthTools.Login has been called) to
/// every request — none of the individual tool methods need to know
/// anything about auth. Registered via AddHttpClient&lt;LoanPlatformApiClient&gt;
/// in Program.cs, so the HttpClient itself is DI-managed (pooled
/// handlers, no manual disposal needed).
/// </summary>
public class LoanPlatformApiClient
{
    private readonly HttpClient _http;
    private readonly ServiceEndpoints _endpoints;
    private readonly TokenStore _tokenStore;

    public LoanPlatformApiClient(HttpClient http, IOptions<ServiceEndpoints> endpoints, TokenStore tokenStore)
    {
        _http = http;
        _endpoints = endpoints.Value;
        _tokenStore = tokenStore;
    }

    private string BaseUrl(LoanPlatformService service) => service switch
    {
        LoanPlatformService.Origination => _endpoints.Origination,
        LoanPlatformService.Underwriting => _endpoints.Underwriting,
        LoanPlatformService.Funding => _endpoints.Funding,
        LoanPlatformService.Servicing => _endpoints.Servicing,
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    public Task<ApiResult> GetAsync(LoanPlatformService service, string path) =>
        SendAsync(HttpMethod.Get, service, path);

    public Task<ApiResult> PostAsync(LoanPlatformService service, string path, object? body = null) =>
        SendAsync(HttpMethod.Post, service, path, body);

    public Task<ApiResult> PutAsync(LoanPlatformService service, string path, object? body = null) =>
        SendAsync(HttpMethod.Put, service, path, body);

    private async Task<ApiResult> SendAsync(HttpMethod method, LoanPlatformService service, string path, object? body = null)
    {
        var url = $"{BaseUrl(service).TrimEnd('/')}/{path.TrimStart('/')}";

        try
        {
            using var request = new HttpRequestMessage(method, url);

            // All business endpoints across all four services require this
            // (see Common/Auth/RequireJwtAuthAttribute.cs) — only
            // /api/health, /api/docs.json, and /api/auth/login are public.
            if (_tokenStore.Token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenStore.Token);

            if (body is not null)
                request.Content = JsonContent.Create(body);

            using var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            JsonElement? parsed = null;
            if (!string.IsNullOrWhiteSpace(content))
            {
                try { parsed = JsonDocument.Parse(content).RootElement.Clone(); }
                catch (JsonException) { /* non-JSON body (e.g. an unhandled 500 HTML page) — surface raw text instead */ }
            }

            return new ApiResult(
                (int)response.StatusCode,
                response.IsSuccessStatusCode,
                parsed,
                parsed is null && !string.IsNullOrWhiteSpace(content) ? content : null);
        }
        catch (HttpRequestException ex)
        {
            return new ApiResult(0, false, null,
                $"Could not reach the {service} service at {BaseUrl(service)}: {ex.Message}. Is it running?");
        }
    }
}
