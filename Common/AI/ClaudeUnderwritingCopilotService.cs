using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.AI;

/// <summary>
/// Calls the Anthropic Messages API to produce an internal-only summary of
/// an application for a human underwriter — same hand-rolled HttpClient
/// approach as ClaudeDocumentExtractionService (no SDK dependency), same
/// tool_choice-forced structured output.
///
/// Two things distinguish this from document extraction, both deliberate:
///
/// 1. This is text-in/text-out (two JSON blobs the caller already has —
///    the application record and this service's own decision record),
///    not an image/PDF content block. Nothing here is ever sent as a
///    document image.
///
/// 2. A "system" prompt carries the advisory-only constraint, kept
///    separate from the user-turn content on purpose: the application
///    JSON embedded in the user turn is data that flows in from an
///    applicant-submitted record, and keeping the constraint outside that
///    turn means it isn't sitting in the same text an adversarial or
///    malformed field could try to talk over. Claude is instructed to
///    work only from the two JSON blobs it's given and never state or
///    imply an approve/deny recommendation — enforced by the prompt, and
///    by UnderwritingCopilotSchemas having no recommendation/decision
///    field to fill in in the first place. This output is never shown to
///    or sent to the applicant — internal underwriter reference only.
///
/// Configuration: reads Anthropic:ApiKey (Anthropic__ApiKey as an env
/// var) and optionally Anthropic:Model (defaults to claude-sonnet-5) —
/// same keys as OriginationService's ClaudeDocumentExtractionService,
/// since this is meant to be the same Anthropic account/key reused across
/// services, not a second one to provision. Degrades gracefully (returns
/// a clear error, doesn't crash the service) when the key is missing —
/// same philosophy as the missing Email:To case elsewhere in this project.
/// </summary>
public class ClaudeUnderwritingCopilotService : IClaudeUnderwritingCopilotService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string SystemPrompt =
        "You are assisting a human loan underwriter by summarizing one application for their own internal " +
        "review. You are not making a lending decision and must never state or imply whether the application " +
        "should be approved or denied, or restate/endorse a decision that has already been made — the human " +
        "underwriter makes that call, not you. Work strictly from the two JSON documents you are given in the " +
        "next message: the application record and the underwriting service's own decision/risk record. Never " +
        "invent, estimate, or infer a number, date, or fact that isn't present in that data — if something " +
        "relevant is simply absent, say so rather than filling the gap. This summary is for the underwriter's " +
        "eyes only and is never shown or sent to the applicant.";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ClaudeUnderwritingCopilotService> _logger;

    public ClaudeUnderwritingCopilotService(HttpClient http, IConfiguration config, ILogger<ClaudeUnderwritingCopilotService> logger)
    {
        _http = http;
        // BUG FIX: no timeout meant a stalled network path to
        // api.anthropic.com (this is the first thing in this service that
        // ever needs outbound internet access — everything else it does is
        // internal-only) left the co-pilot button stuck on "Generating…"
        // for up to HttpClient's default 100-second timeout, or longer if
        // the underlying hang was below that layer. 45 seconds is generous
        // for a real Messages API call (this is a small, fast text
        // request, not a large document) while still resolving to a clear
        // error — not an indefinite spinner — in well under a minute.
        _http.Timeout = TimeSpan.FromSeconds(45);
        _config = config;
        _logger = logger;
    }

    public async Task<UnderwritingCopilotResult> GenerateSummaryAsync(string applicationId, string applicationJson, string decisionJson)
    {
        var apiKey = _config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("REPLACE-WITH", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Co-pilot summary requested for {ApplicationId} but Anthropic:ApiKey is not configured — skipping.", applicationId);
            return new UnderwritingCopilotResult(false, null,
                "Anthropic API key is not configured on this service (set Anthropic__ApiKey).");
        }

        var tool = UnderwritingCopilotSchemas.Build();

        var requestBody = new JsonObject
        {
            ["model"] = _config["Anthropic:Model"] ?? "claude-sonnet-5",
            ["max_tokens"] = 1536,
            ["system"] = SystemPrompt,
            ["tools"] = new JsonArray(tool),
            // Forces Claude to call exactly this tool — the response is
            // guaranteed to be a tool_use block matching the schema, not
            // free-form prose that happens to look like JSON.
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = UnderwritingCopilotSchemas.ToolName },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = $"APPLICATION RECORD (JSON):\n{applicationJson}\n\n" +
                               $"UNDERWRITING DECISION / RISK RECORD (JSON):\n{decisionJson}\n\n" +
                               $"Call {UnderwritingCopilotSchemas.ToolName} with your summary of the above.",
            }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException is what HttpClient actually throws on a
            // timeout (wrapping a TimeoutException) — see the constructor's
            // remarks on why catching only HttpRequestException let a
            // hung/slow connection propagate as an unhandled exception
            // instead of this clean result.
            var reason = ex is TaskCanceledException ? $"timed out after {_http.Timeout}" : ex.Message;
            _logger.LogError(ex, "Could not reach the Anthropic API for {ApplicationId} ({Reason})", applicationId, reason);
            return new UnderwritingCopilotResult(false, null, $"Could not reach the Anthropic API: {reason}");
        }

        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API returned {StatusCode} for {ApplicationId}: {Body}", (int)response.StatusCode, applicationId, responseText);
            return new UnderwritingCopilotResult(false, null,
                $"Anthropic API error ({(int)response.StatusCode}): {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);

        if (!doc.RootElement.TryGetProperty("content", out var contentArray))
        {
            _logger.LogWarning("Anthropic API response had no content array for {ApplicationId}: {Body}", applicationId, responseText);
            return new UnderwritingCopilotResult(false, null, "Unexpected response shape from Anthropic API — see service logs.");
        }

        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_use")
            {
                // .Clone() detaches this from the parent JsonDocument, which
                // is about to be disposed at the end of this method.
                var input = block.GetProperty("input").Clone();
                return new UnderwritingCopilotResult(true, input, null);
            }
        }

        // Shouldn't happen with tool_choice forced to a specific tool, but
        // surfacing this as data rather than throwing keeps the caller in
        // control of how to react.
        _logger.LogWarning("Anthropic API response contained no tool_use block despite forced tool_choice for {ApplicationId}: {Body}", applicationId, responseText);
        return new UnderwritingCopilotResult(false, null,
            "Claude did not return a structured summary. This is unexpected with tool_choice forced — check the raw response in service logs.");
    }
}
