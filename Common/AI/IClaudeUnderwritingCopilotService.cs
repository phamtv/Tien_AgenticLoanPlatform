using System.Text.Json;

namespace LoanPlatform.Common.AI;

/// <summary>
/// Result of one co-pilot summary call. Errors are data, not exceptions —
/// same convention as DocumentExtractionResult and the MCP server's
/// ApiResult: a missing API key or an unreachable Origination service is
/// meaningful information for the caller to show the underwriter, not a
/// thrown exception.
/// </summary>
public record UnderwritingCopilotResult(bool Success, JsonElement? Data, string? Error);

public interface IClaudeUnderwritingCopilotService
{
    /// <param name="applicationId">For logging/tracing only — not sent to Claude as anything other than context.</param>
    /// <param name="applicationJson">Raw JSON body of the application, exactly as returned by OriginationService's GET /api/applications/{id}.</param>
    /// <param name="decisionJson">Raw JSON of this service's own UnderwritingRecord — the decision and risk figures already on file.</param>
    Task<UnderwritingCopilotResult> GenerateSummaryAsync(string applicationId, string applicationJson, string decisionJson);
}
