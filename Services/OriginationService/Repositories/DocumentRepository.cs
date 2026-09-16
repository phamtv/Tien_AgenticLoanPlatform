using System.Collections.Concurrent;

namespace OriginationService.Repositories;

// ExtractedDataJson is optional and defaults to null — existing callers
// (including the MCP server's RecordDocumentUpload tool) that only send
// FileName/DocumentType keep working unchanged; it's populated only when
// a document went through the Claude extraction-and-review flow below.
public record ApplicationDocument(string DocumentId, string ApplicationId, string FileName, string DocumentType, DateTimeOffset UploadedAt, string? ExtractedDataJson = null);

public interface IDocumentRepository
{
    ApplicationDocument Add(ApplicationDocument document);
    IReadOnlyList<ApplicationDocument> GetForApplication(string applicationId);
}

public class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<string, List<ApplicationDocument>> _documents = new();

    public ApplicationDocument Add(ApplicationDocument document)
    {
        _documents.GetOrAdd(document.ApplicationId, _ => []).Add(document);
        return document;
    }

    public IReadOnlyList<ApplicationDocument> GetForApplication(string applicationId) =>
        _documents.GetValueOrDefault(applicationId, []);
}
