using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.AI;

/// <summary>
/// Calls the Anthropic Messages API directly over HttpClient — no SDK
/// dependency, same reasoning as this repo's hand-rolled JWT
/// implementation: nothing here needs more than the base class library
/// plus one HTTP call, so there's no NuGet surface to go stale on us the
/// way the MCP SDK did.
///
/// Structured output is forced via tool_choice pointing at exactly one
/// tool (see DocumentExtractionSchemas) — the same principle as the
/// OpenAI Structured Outputs pattern from earlier in this project,
/// expressed through Claude's tool-use mechanism instead of
/// response_format.
///
/// Configuration: reads Anthropic:ApiKey (Anthropic__ApiKey as an env
/// var) and optionally Anthropic:Model (defaults to claude-sonnet-5). A
/// missing key degrades gracefully rather than crashing the service —
/// same philosophy as SmtpEmailService's missing Email:To — since this
/// is an optional add-on feature, not core to the loan pipeline itself.
/// </summary>
public class ClaudeDocumentExtractionService : IClaudeDocumentExtractionService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ClaudeDocumentExtractionService> _logger;

    public ClaudeDocumentExtractionService(HttpClient http, IConfiguration config, ILogger<ClaudeDocumentExtractionService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(byte[] fileBytes, string contentType, string documentType)
    {
        var apiKey = _config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("REPLACE-WITH", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Document extraction requested but Anthropic:ApiKey is not configured — skipping.");
            return new DocumentExtractionResult(false, documentType, null,
                "Anthropic API key is not configured on this service (set Anthropic__ApiKey).");
        }

        JsonObject tool;
        try
        {
            tool = DocumentExtractionSchemas.Build(documentType);
        }
        catch (ArgumentException ex)
        {
            return new DocumentExtractionResult(false, documentType, null, ex.Message);
        }

        var toolName = tool["name"]!.ToString();
        var base64 = Convert.ToBase64String(fileBytes);
        var isPdf = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

        // PDFs go in as a "document" content block; everything else
        // (image/jpeg, image/png, etc. — a phone photo of a pay stub is
        // just as common in practice as a scanned PDF) goes in as "image".
        var contentBlock = isPdf
            ? new JsonObject
            {
                ["type"] = "document",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "application/pdf", ["data"] = base64 },
            }
            : new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = contentType, ["data"] = base64 },
            };

        var requestBody = new JsonObject
        {
            ["model"] = _config["Anthropic:Model"] ?? "claude-sonnet-5",
            ["max_tokens"] = 2048,
            ["tools"] = new JsonArray(tool),
            // Forces Claude to call exactly this tool — the response is
            // guaranteed to be a tool_use block matching the schema, not
            // free-form prose that happens to look like JSON.
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = toolName },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    contentBlock,
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Extract the fields from this {documentType.Replace('_', ' ')} using the {toolName} tool. " +
                                   "Only use values explicitly visible in the document — if a field is unreadable or " +
                                   "absent, use null for it and add its name to confidenceFlags.",
                    }
                ),
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
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach the Anthropic API");
            return new DocumentExtractionResult(false, documentType, null, $"Could not reach the Anthropic API: {ex.Message}");
        }

        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API returned {StatusCode}: {Body}", (int)response.StatusCode, responseText);
            return new DocumentExtractionResult(false, documentType, null,
                $"Anthropic API error ({(int)response.StatusCode}): {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);

        if (!doc.RootElement.TryGetProperty("content", out var contentArray))
        {
            _logger.LogWarning("Anthropic API response had no content array: {Body}", responseText);
            return new DocumentExtractionResult(false, documentType, null, "Unexpected response shape from Anthropic API — see service logs.");
        }

        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_use")
            {
                // .Clone() detaches this from the parent JsonDocument, which
                // is about to be disposed at the end of this method.
                var input = block.GetProperty("input").Clone();
                return new DocumentExtractionResult(true, documentType, input, null);
            }
        }

        // Shouldn't happen with tool_choice forced to a specific tool, but
        // surfacing this as data rather than throwing keeps the caller in
        // control of how to react (e.g. show the user "try again").
        _logger.LogWarning("Anthropic API response contained no tool_use block despite forced tool_choice: {Body}", responseText);
        return new DocumentExtractionResult(false, documentType, null,
            "Claude did not return a structured extraction. This is unexpected with tool_choice forced — check the raw response in service logs.");
    }
}
