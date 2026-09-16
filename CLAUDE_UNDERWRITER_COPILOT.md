# Feature: Underwriter Co-Pilot

Click "Generate summary" on any application with a decision on file, and
Claude reads the application and its underwriting/risk record, then
returns a short internal summary — risk factors, inconsistencies, and
suggested stipulations — for the underwriter's own reading. It never
states or implies an approve/deny recommendation, and it's never shown or
sent to the applicant.

## Verification status

Same honesty rule as the rest of this project: **this was written against
your real controllers, repositories, and Angular component, but never
compiled or run** (same no-`dotnet`-SDK sandbox constraint as the MCP
server and the document-extraction feature). Everything below is
source-grounded, not blind guessing — but build it and test it before
trusting it. The one thing that genuinely can't be verified without a real
API key: whether Claude's actual response shape from a live
`tool_choice`-forced call matches what `ClaudeUnderwritingCopilotService`
expects to parse. That parsing logic mirrors `ClaudeDocumentExtractionService`,
which carries the same caveat — "documented" and "tested against a real
response" are different levels of confidence.

## What changed

**New files:**
- `Common/AI/UnderwritingCopilotSchemas.cs` — the Claude tool-use schema (summary, riskFactors, inconsistencies, suggestedStipulations — deliberately no recommendation/decision field)
- `Common/AI/IClaudeUnderwritingCopilotService.cs` — interface + result record
- `Common/AI/ClaudeUnderwritingCopilotService.cs` — calls the Anthropic Messages API directly over `HttpClient`, same hand-rolled approach as `ClaudeDocumentExtractionService`, with a `system` prompt carrying the advisory-only constraint
- `Services/UnderwritingService/OriginationApiClient.cs` — the one synchronous, cross-service HTTP call in this service (everything else here is event-driven); forwards the caller's own bearer token to Origination rather than acquiring a separate one, since both services validate against the same Entra ID app registration
- `Tests/UnderwritingService.Tests/CopilotSummaryTests.cs` — integration tests against fakes for both the Origination call and the Claude call, covering the no-decision-yet case, first-call-hits-Claude, second-call-is-cached (the actual cost-control behavior this feature was built around), explicit regenerate, and Origination-unreachable

**Modified:**
- `Services/UnderwritingService/Controllers/UnderwritingController.cs` — new `POST /api/underwriting/{applicationId}/copilot-summary` endpoint (optional `?regenerate=true`)
- `Services/UnderwritingService/Repositories/DecisionRepository.cs` + `SqlDecisionRepository.cs` — `UnderwritingRecord` gained `CopilotSummaryJson` / `CopilotGeneratedAt`, plus a new `UpdateCopilotSummary` repository method (same "update one field in place" pattern as the existing `MarkFunded`)
- `Services/UnderwritingService/Data/UnderwritingDbContext.cs` — matching columns on `UnderwritingDecisionEntity`
- `Services/UnderwritingService/Program.cs` — registers `IClaudeUnderwritingCopilotService` and `IOriginationApiClient` as typed `HttpClient`s
- `Services/UnderwritingService/appsettings.json` — new `Services:Origination` and `Anthropic:ApiKey` / `Anthropic:Model` config
- `docker-compose.yml` — `underwriting` service gains `Services__Origination`, `Anthropic__ApiKey`, and a soft `depends_on: origination`
- `ui/src/app/app.ts` / `app.html` / `app.css` — a co-pilot panel inside each application row's existing "Underwriting Decision" card

## Why manual, not automatic

This was a deliberate call, not a default: the summary is only ever
generated when a human clicks a button, never automatically for every
application that reaches underwriting. Automatic generation would mean
paying for an Anthropic API call on every single application regardless
of whether anyone ever looks at it. On top of that, the result is cached
on the decision record — reopening the same application's review screen
shows the previously generated summary instead of calling Claude again;
only an explicit "Regenerate" click (or `?regenerate=true`) triggers a
fresh call. A re-evaluation (`POST .../evaluate`) clears the cached
summary, since it was generated against the old decision/risk figures and
would be stale context against a freshly re-evaluated one.

## Why this is advisory, not a decision

The Claude tool schema (`UnderwritingCopilotSchemas`) has no
recommendation or approve/deny field anywhere in it — that's enforced at
the schema level, not just requested in the prompt. The `system` prompt
additionally instructs Claude to work strictly from the two JSON records
it's given (the application, and this service's own decision/risk record)
and never invent a number or fact that isn't present in that data. The
output is meant to help a human underwriter read an application faster —
flagging a thin employment history, a DTI near the policy line, a mismatch
between two fields — not to make or restate the lending decision itself.
It's shown only inside the internal review UI, never surfaced to or sent
to the applicant; conflating this with a formal adverse-action notice
(which has its own legal requirements about what it must say) would be a
mistake — they're different features even where both happen to touch a
denial.

## Setup

**1. Set the same Anthropic API key already used by document extraction**
(or a new one — same account is fine) in your `.env`:
```
ANTHROPIC_API_KEY=sk-ant-your-real-key-here
```
This is read by both `OriginationService` (document extraction) and
`UnderwritingService` (this feature) — one key, reused, not two to manage.

**2. No key set?** Same graceful-degradation pattern as document
extraction: the endpoint returns a clear `422` rather than crashing the
service. The rest of underwriting keeps working; only this one feature is
unavailable.

**3. Rebuild and restart Underwriting** (and Origination, if you haven't
already rebuilt it for document extraction) — `docker compose up -d --build underwriting`
if you're on Docker, or a normal rebuild for local `dotnet run`.

**4. Try it**: open the UI's Applications tab, expand any application that
already has a decision, and click "Generate summary" under Underwriting
Decision.

## MCP server

`UnderwritingTools.GetCopilotSummary(applicationId, regenerate = false)`
wraps this endpoint exactly the way `GetDecision`/`GetRiskScore` wrap
theirs — same caching behavior as the UI button: free on a cache hit, a
real (billed) Claude call on a cache miss or `regenerate: true`. There's no
separate read-only/no-cost variant — an MCP client asking for this is a
deliberate action, the same accountability model as a person clicking the
button, so it reuses the one endpoint rather than adding a second one.

## BUG FIX: unbounded timeouts on both outbound calls

Neither `OriginationApiClient` nor `ClaudeUnderwritingCopilotService`
originally set an `HttpClient.Timeout`, which meant a real network problem
(this is the first thing in this project that gives Underwriting outbound
internet access at all, and the first synchronous call it ever makes to
Origination) could leave someone watching the "Generate summary" button
sit on "Generating…" for up to .NET's default 100-second `HttpClient`
timeout — or longer, since a stalled TCP handshake some environments don't
fail fast on isn't necessarily bounded by that timer the way a normal
request/response is. Both clients now set an explicit timeout (15s for the
internal Origination call, 45s for the external Anthropic call) and catch
`TaskCanceledException` alongside `HttpRequestException` — a timeout
throws the former, not the latter, so the original `catch` blocks let a
hang propagate as an unhandled exception instead of the clean error result
callers expect. Worth knowing: `ClaudeDocumentExtractionService` (the
document-extraction feature) has this same unbounded-timeout gap and
wasn't touched here — same fix would apply there if it turns out to matter
in practice.

## Known limitations / good next steps

- **No retry-on-partial-failure UX** — if Claude's call fails, the error
  banner shows and the button goes back to "Generate summary"; there's no
  distinct "try again" state.
- **Cache has no expiry or invalidation beyond re-evaluation** — if the
  *application* data in Origination changes after a summary was generated
  (an updated document, say) without a re-evaluation happening in
  Underwriting, the cached summary won't reflect that change until someone
  clicks Regenerate.
- **Model pinned to `claude-sonnet-5`** by default — configurable via
  `Anthropic:Model`, same as document extraction.
