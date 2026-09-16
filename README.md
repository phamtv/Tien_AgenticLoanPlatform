# Loan Origination Platform — 4-Service Event-Driven Demo

A full-stack demo of an automobile lending platform: four independent
C#/.NET microservices communicating via events, a real (parsed) SQL
Server schema per service, and an Angular UI with a tab per service.
Built to demonstrate the actual stack the target role requires
(C#/.NET Core + Angular + SQL Server + Azure), following the same
verify-what-you-can, document-what-you-can't pattern as the rest of this
project.

## What was actually verified — read this first

Unlike some of the earlier demos in this broader project, **the entire
event chain was proven end-to-end with real running processes**, not just
compiled:

- All 6 .NET projects compile cleanly — 0 warnings, 0 errors
- All 4 services started as real separate processes and passed health checks
- A real application was submitted and traced through all four services:
  ```
  Submit (Origination) → credit pull (Experian, live simulated call) →
  identity check (LexisNexis, live simulated call) →
  ApplicationReadyForUnderwritingEvent published over HTTP →
  Underwriting received it, ran the risk engine, decision: Approved
  (score 831, $20,000 at 5.9%) →
  UnderwritingDecisionEvent published →
  Funding received it, disbursed LOAN-D39BC019 →
  LoanFundedEvent published →
  Servicing received it, activated the loan
  ```
  Verified with the actual JSON responses from all four services, not
  just log lines claiming success.
- A denial path was also verified — a different application had its
  identity check simulated-fail, Underwriting correctly denied it, and
  Funding correctly did nothing in response (proving the event-filtering
  logic, not just the happy path).
- **A real bug was found and fixed during this process**: `ContentRootPath`
  defaults to the process's working directory, not the DLL's own folder —
  meaning `appsettings.json` (and the event subscriber configuration in
  it) silently failed to load when a service was launched from the repo
  root. Fixed by launching each service from its own output directory.
  Worth knowing if you run this yourself.
- **CORS was verified with a real preflight request** (`curl -X OPTIONS`
  with `Origin`/`Access-Control-Request-*` headers, the same thing a
  browser sends before a cross-origin `POST`) — confirmed the API
  correctly returns `Access-Control-Allow-Origin`, so the Angular UI
  genuinely works from a browser, not just via `curl` (which doesn't
  enforce CORS the way a browser does).
- The Angular UI was built for real (`ng build`, not just written) —
  caught and fixed a real build failure along the way (Angular tries to
  inline Google Fonts at build time; blocked by this sandbox's network
  restrictions, fixed by removing the external font dependency).
- **Every endpoint across all four services was implemented and spot-tested
  against real running processes** — not just the core event-driven path.
  This includes standalone/manual-trigger endpoints that exist alongside
  the event-driven flow (e.g. `POST /api/fundings` to fund outside the
  event chain, `POST /api/underwriting/{id}/evaluate` to manually trigger
  a decision), plus read endpoints like `GET /api/loans/{id}/schedule`,
  which computes a **real amortization schedule** — verified by hand: an
  $18,000 loan at 9.9% over 6 months produced a $3,087.22/month payment
  and correctly reached exactly $0 remaining balance at month 6.
- **Email notifications, one per service, verified against a real SMTP
  connection** — a local test SMTP server (Python's `aiosmtpd`) was stood
  up and all four services pointed at it. Submitting one real application
  produced four real emails, captured with their actual headers and
  content: "Loan Application Submitted" (Origination), "Loan APPROVED"
  (Underwriting, with the decision reasoning), "Loan Funded" (Funding,
  with disbursement details), "Loan Activated for Servicing" (Servicing).
  Not simulated or logged-and-assumed — an actual SMTP `DATA` command
  captured on the wire.
- **JWT authentication on every business endpoint, added after an initial
  gap was caught** — all four services originally shipped with zero auth.
  Fixed using the same hand-rolled HS256 JWT pattern proven in
  `backend-dotnet/` earlier in this broader project (no NuGet access, so
  `System.Security.Cryptography` directly rather than
  `Microsoft.AspNetCore.Authentication.JwtBearer`). All four services
  share one signing secret, so logging in once at any single service
  issues a token valid at all four — verified for real: logged in against
  Origination, then used that same token successfully against
  Underwriting, Funding, and Servicing without a second login. Also
  verified: a request with no token gets `401`, a garbage token gets
  `401`, wrong credentials get `401`, and `/api/health`+`/api/docs.json`
  correctly remain public. The Angular UI was updated to match — a login
  screen gates the dashboard, and every API call attaches the token.

**What was not verified**: the SQL schemas were validated with a real
T-SQL parser (`sqlfluff`) but never run against a live SQL Server
instance (none was available). The EF Core data layer and the Azure
Service Bus event bus are both written as documented reference code, not
compiled — same NuGet-access constraint as `backend-dotnet/` elsewhere in
this project. See `Database/README.md` and
`Common/EventBus/AzureServiceBusEventBus.reference.cs.txt` for exactly
what's unverified and why.

## Architecture

```
loan-platform/
├── LoanPlatform.sln
├── Contracts/              ← shared event/model definitions (ProjectReference from all 4 services)
├── Common/                  ← shared infra: event bus, key vault, logging, vendor integrations
│   ├── EventBus/               IEventBus, HttpLoopbackEventBus (working), AzureServiceBusEventBus (reference)
│   ├── KeyVault/                same env/azure pattern as backend-dotnet
│   └── Vendors/                  Experian, TransUnion, LexisNexis, TrueID, Informed
├── Services/
│   ├── OriginationService/     (port 5100) intake + vendor calls, publishes ApplicationReadyForUnderwritingEvent
│   ├── UnderwritingService/    (port 5101) risk engine, publishes UnderwritingDecisionEvent
│   ├── FundingService/         (port 5102) disbursement, publishes LoanFundedEvent
│   └── ServicingService/       (port 5103) loan + payment records
├── Database/                ← SQL Server schema, one file per service
├── ui/                      ← Angular dashboard, one tab per service
└── .github/workflows/       ← dependency-aware CI/CD (shared-lib changes rebuild all 4)
```

### Why this event bus design

Each service depends only on `IEventBus` — not on any specific transport.
Locally, `HttpLoopbackEventBus` delivers events over plain HTTP POST
between the four running processes, which is what let the whole chain
above actually be tested without an Azure subscription. In production,
`AzureServiceBusEventBus` (written, documented, not yet compiled/run)
would be swapped in via a one-line DI change in each service's
`Program.cs` — no application code changes.

## Running it yourself

**Backend (4 services, from the repo root):**
```bash
cd Services/OriginationService/bin/Debug/net8.0 && ASPNETCORE_URLS=http://127.0.0.1:5100 JwtSecret=change-this-to-something-long DemoUsername=demo DemoPassword=demo12345 dotnet OriginationService.dll &
cd Services/UnderwritingService/bin/Debug/net8.0 && ASPNETCORE_URLS=http://127.0.0.1:5101 JwtSecret=change-this-to-something-long DemoUsername=demo DemoPassword=demo12345 dotnet UnderwritingService.dll &
cd Services/FundingService/bin/Debug/net8.0 && ASPNETCORE_URLS=http://127.0.0.1:5102 JwtSecret=change-this-to-something-long DemoUsername=demo DemoPassword=demo12345 dotnet FundingService.dll &
cd Services/ServicingService/bin/Debug/net8.0 && ASPNETCORE_URLS=http://127.0.0.1:5103 JwtSecret=change-this-to-something-long DemoUsername=demo DemoPassword=demo12345 dotnet ServicingService.dll &

# JwtSecret must be identical across all four for the shared-token behavior to work.
```
(Or `dotnet build LoanPlatform.sln` first if you haven't built it yet —
note each service's own output folder is where `appsettings.json` needs
to be loaded from, per the ContentRootPath issue above — always launch
from inside `bin/Debug/net8.0`, not the repo root.)

**Frontend:**
```bash
cd ui
npm install
ng serve
```
Open `http://localhost:4200`.

## Deployment architecture — 5 separate containers, not one

`docker-compose.yml` spins up the **whole platform as 5 independent
containers** — one per service, plus one for the UI — not everything
bundled into a single image. That's deliberate, not incidental: one
container per service is what actually makes this a microservices
architecture rather than a monolith wearing four different port numbers.
Packing all four .NET services into one container would mean losing
independent scaling, independent deployment, and fault isolation between
them — exactly the properties the event-driven design elsewhere in this
README is built around.

```bash
cp .env.example .env   # optional — sensible defaults exist without this
docker compose up --build
```
Open `http://localhost:4200`.

Two different networks are in play, worth understanding if you're
debugging connectivity:
- **Browser ↔ services**: over the **host** network, via each
  container's published port (`localhost:5100`-`5103`, `localhost:4200`)
  — this is what the Angular bundle's hardcoded service URLs expect, and
  doesn't change between running locally and running in Docker.
- **Service ↔ service (the event bus)**: over **Docker's internal**
  network, using each container's Compose service name as its DNS
  hostname (`http://underwriting:8080`, not `http://localhost:5101`) —
  overridden via `EventSubscribers__*` environment variables in
  `docker-compose.yml`, since the `appsettings.json` defaults
  (`localhost:510X`) are correct for running the four services as local
  processes but wrong inside containers, where `localhost` means the
  container itself.

**Verification status, same honest pattern as the Dockerfiles
themselves**: `docker-compose.yml` was validated as syntactically correct
YAML with the right service/build/port structure, but — like every other
Docker-related piece across this whole broader project — no live Docker
daemon was available in the sandbox this was built in, so `docker compose
up` itself has not been run. Worth a real test on your end before
treating it as fully proven.

## CI/CD

See `.github/workflows/main.yml` — a dependency-aware orchestrator. A
change to `Contracts/` or `Common/` (shared by all four services)
triggers rebuilding all four; a change scoped to one service's own folder
only rebuilds that one. `build-service.yml` is one reusable, parameterized
workflow rather than four near-duplicate files.

Not yet added: a CI job for the Angular UI itself, or Docker builds for
it wired into the orchestrator (the UI has a `Dockerfile` but isn't yet
part of `main.yml`'s change detection).

## Email notifications

Each service sends one review email at the end of its own step —
Origination on submission, Underwriting on decision (approved or denied),
Funding on disbursement, Servicing on loan activation. Uses
`System.Net.Mail.SmtpClient`, part of the .NET base class library (not a
NuGet package), so this works with no external dependency.

**Configure your real recipient** in each service's `appsettings.json`
under `"Email": { "To": "..." }`, or via environment variable at launch
(e.g. `Email__To=your.email@example.com` — .NET's standard `__` syntax
for nested config keys, exactly what was used to verify this).

A missing `Email:To` doesn't crash the service or fail the business
operation that triggered it — it logs a warning and moves on, same
graceful-degradation principle used for vendor calls and event publishing
elsewhere in this project.

**Worth knowing:** `SmtpClient` is functional but Microsoft recommends
`MailKit` (a NuGet package) for production use, mainly over better modern
TLS/auth support. Swapping it in is a one-line DI change in each
`Program.cs`, same pattern as the Key Vault and Service Bus abstractions.

## Authentication

All business endpoints across all four services require a Bearer JWT —
only `/api/health`, `/api/docs.json`, and `/api/auth/login` are public.
Log in once, at any single service:

```bash
curl -X POST http://localhost:5100/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"demo","password":"demo12345"}'
```

The returned token works at **all four services** — they share one
signing secret (`JwtSecret`, loaded via the key vault abstraction at
startup, same as the rest of this project), modeling a shared-identity
pattern rather than four separate logins.

```bash
curl http://localhost:5100/api/applications -H "Authorization: Bearer <token>"
curl http://localhost:5103/api/loans -H "Authorization: Bearer <token>"
```

Same hand-rolled HS256 JWT implementation as `backend-dotnet/` elsewhere
in this project (`System.Security.Cryptography`, not
`Microsoft.AspNetCore.Authentication.JwtBearer`, for the same NuGet-access
reason). Demo credentials (`demo` / `demo12345`) are set via
`DemoUsername`/`DemoPassword` config, same as `JwtSecret` — change these
before treating this as anything beyond a local demo.

## Vendor integrations

`Common/Vendors/` — `ICreditBureauService` (Experian, TransUnion) and
`IIdentityVerificationService` (LexisNexis, TrueID, Informed), matching
the vendors named in the interview prep discussion for this role. Each
implementation simulates a realistic response with a network-call delay,
since no real vendor API credentials exist for this demo — each file's
doc comment explains what the real integration would actually require.
