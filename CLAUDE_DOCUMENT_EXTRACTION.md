# Feature: Claude-Powered Document Extraction

Upload a pay stub, W-2, bank statement, or ID from the Origination tab —
Claude reads it and returns structured fields for you to review and
correct before saving them against the application.

## Verification status

Same honesty rule as the rest of this project: **this was written against
your real controllers and Angular component, but never compiled or run**
(same no-`dotnet`-SDK sandbox constraint as the MCP server). Everything
below is source-grounded, not blind guessing — but build it and test it
before trusting it.

One thing that genuinely can't be verified without a real API key and a
real document: whether Claude's actual response shape from a live
`tool_choice`-forced call matches what `ClaudeDocumentExtractionService`
expects to parse. The parsing logic (walk `content`, find the
`tool_use` block, read `.input`) matches Anthropic's documented Messages
API response shape, but "documented" and "tested against a real response"
are different levels of confidence — flagging that explicitly rather than
overstating it.

## What changed

**New files:**
- `Common/AI/DocumentExtractionSchemas.cs` — Claude tool-use schemas for the four document types
- `Common/AI/IClaudeDocumentExtractionService.cs` — interface + result record
- `Common/AI/ClaudeDocumentExtractionService.cs` — calls the Anthropic Messages API directly over `HttpClient` (no SDK dependency, same reasoning as this repo's hand-rolled JWT)

**Modified:**
- `Services/OriginationService/Controllers/ApplicationsController.cs` — new `POST /api/applications/{id}/documents/extract` endpoint; `UploadDocumentRequest` gained an optional `ExtractedDataJson` field
- `Services/OriginationService/Repositories/DocumentRepository.cs` + `SqlRepositories.cs` + `Data/Entities.cs` — `ApplicationDocument` carries the verified extraction JSON once saved
- `Services/OriginationService/Program.cs` — registers `IClaudeDocumentExtractionService` as a typed `HttpClient`
- `Services/OriginationService/appsettings.json` — new `Anthropic:ApiKey` / `Anthropic:Model` config
- `ui/src/app/app.ts` / `app.html` / `app.css` — new upload-and-review panel in the Origination tab

## How it works, end to end

1. **User picks an application, a document type, and a file** in the new panel at the bottom of the Origination tab.
2. **Extract with Claude** → uploads via `multipart/form-data` to `POST /api/applications/{id}/documents/extract`.
3. **OriginationService** reads the file into memory, calls `ClaudeDocumentExtractionService.ExtractAsync(...)`, which sends it to `https://api.anthropic.com/v1/messages` as an `image` or `document` content block, with `tool_choice` forcing one of the four extraction tools. **Nothing is persisted at this step** — it's a preview.
4. **The UI shows every extracted field as an editable input**, plus a warning banner listing anything Claude flagged as low-confidence via `confidenceFlags`.
5. **User corrects anything wrong, clicks "Confirm & Save"** → posts the edited fields (serialized back to JSON) to the existing `POST /api/applications/{id}/documents` endpoint, which now accepts and stores `ExtractedDataJson` alongside the file metadata it already tracked.

## Setup

**1. Get an Anthropic API key** — [console.anthropic.com](https://console.anthropic.com)

**2. Set it before starting Origination:**
```bash
Anthropic__ApiKey=sk-ant-your-real-key-here dotnet OriginationService.dll
```
or edit `appsettings.json` directly (fine for local dev, not for anything committed to source control).

**3. No key set?** The endpoint returns a clear `422` with an explanatory message rather than crashing the service — same graceful-degradation pattern as the missing `Email:To` case elsewhere in this project. The rest of the platform keeps working; only this one feature is unavailable.

**4. Rebuild and restart Origination**, then `npm install` isn't needed on the UI side (no new packages — this uses Angular's existing `HttpClient` and `FormData`, both already in use elsewhere in `app.ts`). Just rebuild/reload the UI as usual.

## Known limitations / good next steps

- **File bytes aren't persisted anywhere** — only the extracted+verified *data* is saved (as JSON on the document record), not the original file. If you want the actual pay stub image retrievable later, that's the blob-storage conversation from earlier in this project (S3/Azure Blob) — a real gap worth closing before this is anything beyond a demo.
- **No retry/re-extract UI** — if Claude's first pass is bad, the user has to re-upload the same file rather than clicking "try again" on the same upload.
- **Not exposed through the MCP server** — the `loan-platform` MCP tools don't include this endpoint yet. Straightforward to add (`ExtractDocument` tool on `OriginationTools`, using `multipart/form-data` via `HttpClient`) if you want it callable from Claude Desktop too — just wasn't part of this pass.
- **Model pinned to `claude-sonnet-5`** by default — configurable via `Anthropic:Model` if you want to try a different model for cost/accuracy tradeoffs (Haiku for cheaper/faster on clean documents, for instance).
