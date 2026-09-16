# McpServer — MCP Server for the Loan Origination Platform

Wraps the four running services (Origination, Underwriting, Funding,
Servicing) as MCP tools, so an MCP-aware AI client (Claude Desktop, or any
other MCP client) can drive the platform through natural-language requests
instead of raw `curl` calls — list applications, submit one, check an
underwriting decision, fund a loan, pull an amortization schedule, all as
tool calls with real HTTP requests behind them.

## Verification status — read this first

**This project has not been compiled or run.** Every other honesty note
in this repo ("written as reference code, not compiled — no NuGet access
in that sandbox") had at least a working `dotnet` SDK available. This one
didn't even have that: it was hand-written in a sandbox with no .NET SDK
installed at all, cross-checked line-by-line against the actual
controllers below, but never built.

What that means concretely:

- **Endpoints, HTTP verbs, and request shapes** were copied directly from
  `Services/*/Controllers/*.cs` in this repo — high confidence these
  match, since they're read directly from your source, not guessed from
  the top-level README.
- **The MCP SDK usage pattern** (`[McpServerToolType]`, `[McpServerTool]`,
  `AddMcpServer()`, `WithStdioServerTransport()` / `WithHttpTransport()`,
  `WithToolsFromAssembly()`, typed-client DI via `AddHttpClient<T>()`)
  follows the SDK's documented API surface as of `ModelContextProtocol` /
  `ModelContextProtocol.AspNetCore` 2.2.0, but the SDK is under active
  development (v2.0 was a breaking rewrite of the HTTP transport layer) —
  **build this against the actual NuGet packages before trusting it**, and
  check each package's current docs if anything doesn't compile.
- **Never tested against the real running services** — no request in
  this project has actually round-tripped to `OriginationService` on
  `:5100` (or `http://origination:8080` inside Docker) or any of the
  others.

Treat this the same way the main README treats the EF Core / Azure
Service Bus code: a solid, source-grounded starting point that needs a
real build-and-run pass before you rely on it.

## What it actually does

One tool per controller action across all four services, plus a login
tool:

| Tool class | Wraps | Tool count |
|---|---|---|
| `AuthTools` | Entra ID client-credentials login | 2 |
| `OriginationTools` | `ApplicationsController.cs` | 7 |
| `UnderwritingTools` | `UnderwritingController.cs` | 4 |
| `FundingTools` | `FundingsController.cs` | 5 |
| `ServicingTools` | `LoansController.cs` | 8 |

**Auth to the four business services is handled once, centrally.** Call
`Login` and the resulting Entra ID app-only token is stored in a singleton
`TokenStore` for the life of the MCP server process, then attached as a
Bearer token to every subsequent call across all four services — see
"Two different auth boundaries" below for why this is a separate thing
from the HTTP transport's own access control.

**Errors are data, not exceptions.** Every tool returns an `ApiResult`
with `StatusCode`, `Success`, and either a parsed `Body` or an `Error`
string — a 404 ("no decision yet, still processing") or a 409 ("already
funded") comes back as structured information for whatever's calling
these tools to react to, rather than a thrown exception. A connection
failure (service not running) comes back the same way, with a message
naming which service and URL it tried to reach.

**`SubmitApplication` is flattened.** The real endpoint expects a nested
`{ applicant: {...}, employment: {...}, vehicle: {...} }` body. This tool
takes 20+ flat scalar parameters instead of matching nested objects,
because flat parameters are the safer bet for automatic JSON-schema
generation across MCP SDK versions — the method reconstructs the nested
shape internally before the HTTP call goes out. If you'd rather have
nested tool parameters (cleaner for a human reading the tool list, riskier
for schema generation), that's the one method most worth revisiting once
you can actually compile and test against the SDK.

## Two transports, one codebase

`Program.cs` can run this project two different ways, picked at process
startup by the `Mcp__Transport` environment variable (same runtime-toggle
pattern as `EventBus:UseServiceBus` elsewhere in this repo — a config
value, not a compile-time `#if`, so switching back is instant):

| `Mcp__Transport` | Host | Use case |
|---|---|---|
| unset / `Stdio` (default) | Generic `Host`, stdio transport | Claude Desktop (or any MCP client) launches this as a local subprocess and talks to it over stdin/stdout. Matches how this project has always worked. |
| `Http` | `WebApplication` + Kestrel, streamable HTTP transport | Runs as its own container (`docker-compose.yml`'s `mcp-server` service), reachable over the network at `https://localhost:5105` (or `http://localhost:5104`) on the host, or `http://mcp-server:8080` from inside Docker's network. |

Nothing else about the tool classes changes between the two — same DI
registrations, same `LoanPlatformApiClient`, same tools. Only `Program.cs`
branches.

## Two different auth boundaries

It's easy to conflate these, so to be explicit — there are two separate
things called "auth" in this project, protecting two separate boundaries:

1. **MCP server → the four business services.** Solved by `AuthTools.Login`
   (Entra ID client-credentials / app-only token, stored in `TokenStore`,
   attached to every downstream call). This exists regardless of transport
   and hasn't changed.
2. **MCP client → this MCP server.** Only exists as a real boundary once
   this runs over HTTP. Over stdio, whatever can launch the subprocess
   already has full access by construction — there was never anything to
   check. Over HTTP, anyone who can reach the container's port could call
   every tool (and, transitively, all four services) unless something
   stops them. `RunHttpAsync` in `Program.cs` closes this with a
   shared-secret header check: the caller must send
   `Authorization: Bearer <Mcp__ApiKey value>`, checked with a
   constant-time comparison. This is deliberately **not** the same Entra
   ID bearer validation the four business services use — that validates
   tokens meant to be acquired fresh and to expire in about an hour, which
   is the wrong shape for a static value sitting in an MCP client's config
   file with no token-refresh logic behind it. A long-lived shared secret
   (the same trust model this repo already uses for
   `AZURE_AD_CLIENT_SECRET`) is the honest fit here. Real per-employee
   identity on this endpoint — as opposed to one shared secret for
   whichever client holds it — would be a separate interactive OAuth flow,
   not yet built.

The container **refuses to start** in HTTP mode if `Mcp__ApiKey` /
`MCP_API_KEY` isn't set, rather than silently running unprotected.

## Setup

### Option A — stdio, running locally (unchanged from before)

**1. Add the project to the solution**, if it isn't already:
```bash
dotnet sln LoanPlatform.sln add Services/McpServer/McpServer.csproj
```

**2. Restore and build:**
```bash
cd Services/McpServer
dotnet restore
dotnet build
```

**3. Point it at your services**, if they're not on the default ports —
edit `appsettings.json` or set env vars:
```bash
Services__Origination=http://localhost:5100 \
Services__Underwriting=http://localhost:5101 \
Services__Funding=http://localhost:5102 \
Services__Servicing=http://localhost:5103 \
dotnet run
```

**4. Have all four services already running** (see the main README's
"Running it yourself" section) before pointing an AI client at this.

**5. Register it with an MCP client.** For Claude Desktop, add to its MCP
config (`claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "loan-platform": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/Services/McpServer"]
    }
  }
}
```

### Option B — HTTP, running in its own container

**Important — this is NOT configured through `claude_desktop_config.json`.**
That file (Claude Desktop's Settings > Developer > "Local MCP servers"
panel) is specifically for local, stdio/subprocess servers — an entry
there needs a `command` to launch, which a remote HTTP server doesn't
have. Putting a `url`-shaped entry in it doesn't error, it's just silently
ignored (the panel will still show "No servers added"). Remote MCP servers
go through a completely separate UI: **Settings > Connectors**. Don't lose
time on the config-file route for this option.

**1. Generate a local HTTPS certificate.** Claude's "Add custom connector"
dialog requires an HTTPS server URL — even for `localhost` — so the
container needs to present one Windows already trusts, not a self-signed
one nothing would trust. Run once, in PowerShell:
```powershell
New-Item -ItemType Directory -Force -Path "$env:USERPROFILE\.aspnet\https"
dotnet dev-certs https -ep "$env:USERPROFILE\.aspnet\https\McpServer.pfx" -p <choose-a-password>
dotnet dev-certs https --trust
```
The third command pops a Windows dialog asking to trust the certificate
— click Yes. This is the standard ASP.NET Core local-dev HTTPS pattern
(see [Microsoft's docs](https://learn.microsoft.com/en-us/aspnet/core/security/docker-compose-https)),
not something specific to this project.

**2. Set two values in your `.env`** (repo root):
```
MCP_API_KEY=<a real random value, e.g. openssl rand -hex 32>
MCP_CERT_PASSWORD=<the exact same password you passed to -p above>
```
The container refuses to start without `MCP_API_KEY`. `MCP_CERT_PASSWORD`
not matching the certificate's actual password means Kestrel fails to
load it — you'll see that clearly in the container logs, not a silent
failure.

**3. Bring it up along with everything else:**
```bash
docker compose up --build mcp-server
```
(or just `docker compose up --build` for the whole platform). It now
listens on two host ports: `localhost:5104` over plain HTTP (handy for a
quick local `curl` check, not something Claude's connector can use) and
`localhost:5105` over HTTPS using the certificate from step 1 — that
second one is the actual connector URL.

**If you change `docker-compose.yml` (ports, volumes, environment) after
a container is already running, `docker compose up -d <service>` doesn't
always recreate it — sometimes it just leaves the existing container as
is.** Force it when in doubt:
```bash
docker compose up -d --force-recreate mcp-server
```

**4. Register it as a custom connector** (Settings > Connectors > Add
custom connector — see [Anthropic's docs](https://claude.com/docs/connectors/custom/remote-mcp)
for the exact dialog, which varies by client version):
- **URL**: `https://localhost:5105`
- **Authentication**: "No sign-in"
- **Request headers**: add `Authorization` = `Bearer <your MCP_API_KEY value>`,
  marked Required

Request-header authentication is a beta feature gated per organization —
if the dialog doesn't show a Request headers section at all, that's why,
and there's no client-side workaround for it; it needs Anthropic to
enable it for your org.

## A first smoke test, once you've built it

```
1. Call Login (no args needed — acquires an Entra ID app-only token)
2. Call ListApplications — should return whatever's currently in
   Origination's store (empty array on a fresh service start)
3. Call SubmitApplication with a full set of applicant/employment/vehicle
   details — this should trigger the real credit + identity check calls
   and kick off the same event chain the main README traces by hand
4. Call GetApplicationStatus with the returned ApplicationId — should
   show "UnderwritingInProgress" immediately, then "Approved" or "Denied"
   shortly after, once the event reaches Underwriting
```

If step 1 fails with a connection error, check the service URLs in
`appsettings.json` (or `Services__*` env vars) against what's actually
running. If step 3's response doesn't match what
`OriginationTools.SubmitApplication`'s body shape expects, that's the
nested-vs-flat DTO mismatch flagged above — the first thing to check
line-by-line against `ApplicationsController.SubmitApplicationRequest`.

If you're on the HTTP transport and every call comes back `401`, check
that your client's `Authorization` header exactly matches `Mcp__ApiKey` —
there's no partial-match or fallback.

## Not included here

- **Docker/CI wiring in `.github/workflows/main.yml`** — `mcp-server` is
  in `docker-compose.yml` now, but not yet added to CI. Same reasoning as
  the Angular UI's Dockerfile not yet being in the CI orchestrator: a
  deliberate scope cut, not an oversight, until this itself has been
  proven to work by an actual build-and-run pass.
- **Real per-employee identity on the MCP HTTP endpoint** — the shared
  `Mcp__ApiKey` secret means every caller holding it looks identical to
  this server; there's no concept of "which person is asking" the way
  Entra ID user sign-in would provide. Fine for a small team sharing one
  secret out of band; the interactive OAuth flow that would fix this is
  real, separate work, not a small addition to what's here.
