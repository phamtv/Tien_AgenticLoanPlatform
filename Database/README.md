# Database Layer

Each microservice owns its own SQL Server database — no shared database,
no cross-service foreign keys or joins. This matches the microservices
principle already established elsewhere in this project: services only
know about each other through events (or, for a query, the owning
service's API), never through a shared data store.

## Verification status

The four `.sql` schema files in this folder were **validated with a real
T-SQL parser** (`sqlfluff parse --dialect tsql`) — all four parse as
syntactically valid T-SQL, zero errors. They were **not run against a
real SQL Server instance**, since none was available in the sandbox this
was built in — no live database to execute `CREATE DATABASE` /
`CREATE TABLE` against and confirm.

The actual, running data layer for all four services right now is
**in-memory** (`ConcurrentDictionary`-based repositories, e.g.
`OriginationService/Repositories/ApplicationRepository.cs`) — genuinely
compiled, run, and verified end-to-end (see the root README's test
results: a real application flowed through all four services with real
data). The SQL schemas and the EF Core reference implementation below are
the intended production data layer, honestly labeled as un-executed
rather than implied as proven.

## Files

| File | What it defines |
|---|---|
| `01_origination_schema.sql` | `LoanApplications`, `CreditBureauResults`, `IdentityVerificationResults` |
| `02_underwriting_schema.sql` | `UnderwritingDecisions` (with a CHECK constraint enforcing that a denial can't carry an approved amount/rate) |
| `03_funding_schema.sql` | `Fundings` |
| `04_servicing_schema.sql` | `Loans`, `Payments` |

Each is a standalone script — `CREATE DATABASE IF NOT EXISTS` followed by
its tables, ready to run against a real SQL Server 2012+ instance
(matching the JD's stated minimum version) with `sqlcmd` or SSMS.

## EF Core is now fully wired up — real code, opt-in by default

Every service has a complete, real EF Core implementation now — not
placeholder `.txt` reference files. Each service has:

- `Data/<Service>DbContext.cs` + entity classes, matching the
  corresponding `Database/*.sql` schema exactly
- `Repositories/Sql*Repository.cs`, implementing the exact same
  repository interface the in-memory version implements

**It's opt-in, controlled by an MSBuild property:**

```bash
dotnet build -p:UseSqlServer=true
```

Without that flag (the default), the SQL Server files are excluded from
compilation entirely via `<Compile Remove>` in each `.csproj`, and
`Program.cs` uses `#if USE_SQL_SERVER` / `#else` to register the
in-memory repositories instead. This is deliberate: it means the
default build stays compilable with **zero external NuGet packages** —
verified repeatedly by rebuilding the full solution after every change —
while the real SQL Server code path exists, fully written, ready to
compile the moment you opt in.

`docker-compose.yml` at the repo root builds all four services with
`UseSqlServer: "true"` (via each service's Dockerfile build arg) against
a real `sqlserver` container it also provisions, with connection strings
wired through automatically and a health check so the services wait for
a genuinely connectable database before starting.

**What was actually verified vs. not, stated plainly:**
- The default (in-memory) build was rebuilt and confirmed clean — 0
  warnings, 0 errors, all six projects — after every single file added in
  this SQL Server work, specifically to make sure none of it broke what
  was already working.
- The EF Core code itself has **not** been compiled — this sandbox still
  has no NuGet access (confirmed again: `api.nuget.org` returns `403`
  here), and no Docker daemon, so `docker compose up` has never run
  either. It's real, complete, schema-accurate code — but genuinely
  untested until built somewhere with normal internet access.

## Getting this running for real

```bash
# Easiest: let docker-compose provision SQL Server and build everything
# with UseSqlServer=true automatically
docker compose up --build

# Or manually, once you have a SQL Server instance available:
sqlcmd -S <server> -i Database/01_origination_schema.sql
sqlcmd -S <server> -i Database/02_underwriting_schema.sql
sqlcmd -S <server> -i Database/03_funding_schema.sql
sqlcmd -S <server> -i Database/04_servicing_schema.sql
# (or skip this — EnsureCreated() at startup creates the schema
# automatically from the EF Core model the first time each service runs)

cd Services/OriginationService
dotnet build -p:UseSqlServer=true
```

**One honest design note worth knowing:** `EnsureCreated()` generates
the schema from each `DbContext`'s fluent API model, not from the
`Database/*.sql` files — the two are hand-kept consistent right now, but
they're two separate sources of truth. A real project should pick one
(typically EF Core migrations via `dotnet ef migrations add`) rather than
maintaining both indefinitely.
