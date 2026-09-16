using System.Text.Json;

namespace LoanPlatform.Common.AI;

/// <summary>
/// Result of one extraction call. Errors are data, not exceptions — same
/// convention as this repo's vendor integrations and the MCP server's
/// ApiResult: a missing API key or an unreadable document is meaningful
/// information for the caller to show the user, not a thrown exception.
/// </summary>
public record DocumentExtractionResult(bool Success, string DocumentType, JsonElement? Data, string? Error);

public interface IClaudeDocumentExtractionService
{
    /// <param name="fileBytes">Raw bytes of the uploaded file.</param>
    /// <param name="contentType">
    /// MIME type of the upload — "application/pdf" is sent to Claude as a
    /// document content block; anything else (image/jpeg, image/png, etc.)
    /// is sent as an image content block.
    /// </param>
    /// <param name="documentType">
    /// One of DocumentExtractionSchemas.SupportedTypes — selects which
    /// extraction schema Claude is forced to fill in.
    /// </param>
    Task<DocumentExtractionResult> ExtractAsync(byte[] fileBytes, string contentType, string documentType);
}
