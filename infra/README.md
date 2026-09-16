# Azure Container Apps migration

This folder deploys the Loan Platform (4 services + MCP server + UI + SQL)
to Azure Container Apps, replacing the local `docker-compose.yml` setup for
anything beyond local dev.

**Verification status, same honesty policy as the rest of this repo:**
none of this — `main.bicep`, `deploy.ps1`, the MCP server's HTTP/SSE
conversion, or the CORS/UI config changes — has been run against a real
Azure subscription yet. It's written against documented, current API
schemas and cross-checked against this repo's actual code (connection
string names, config keys, controller routes), not guessed. Validate with
`az deployment group what-if` before your first real deploy, and read
`deploy.ps1` before running it.

## What changed and why

| Area | Before (docker-compose) | After (Azure) | Why |
|---|---|---|---|
| Compute | 6 containers on one Docker host | 6 Azure Container Apps in one Container Apps Environment | Serverless containers, built-in scaling, no host to patch |
| Service discovery | Docker Compose service names (`http://underwriting:8080`) | Container Apps internal FQDN (`https://<app>.internal.<env-domain>`) | Same idea, Azure's equivalent DNS |
| Database | `mssql/server:2022-latest` container | Azure SQL Database (4 databases, one logical server) | Managed backups/HA; no container to keep alive |
| Secrets | `.env` file, plain env vars | Azure Key Vault, referenced via Container Apps `secretRef` | Not stored in plaintext config |
| Auth | ~~JWT_SECRET/DemoUsername/DemoPassword~~ (already dead in code — see below) | Entra ID bearer tokens (unchanged — already the real state) | N/A, just documenting |
| UI → backend URLs | Hardcoded `localhost:5100-5103` in the JS bundle | Fetched from `/config.json`, generated at container start from env vars | Browser isn't on the same machine as the containers anymore |
| CORS | Hardcoded to `http://localhost:4200` | Configurable via `Cors:AllowedOrigins`, set to the UI's real Azure URL | The UI's origin isn't `localhost` anymore |
| MCP server | stdio subprocess, local only, not containerized | HTTP/SSE transport, its own Container App (internal-only ingress) | Stdio has no meaning without a parent process to launch it |
| Image registry | GHCR (`.github/workflows/build-service.yml`) | Azure Container Registry (ACR) | Simpler identity-based pulls from Container Apps; GHCR would also work with a registry secret if you'd rather keep it |

### A pre-existing inconsistency this migration surfaced

Your `docker-compose.yml` still set `JwtSecret`/`DemoUsername`/`DemoPassword`
env vars, but the actual code (`Common/Auth/EntraIdAuthExtensions.cs`,
`Services/McpServer/Tools/AuthTools.cs`) had already moved to real Entra ID
authentication — those three env vars were dead. This migration's updated
`docker-compose.yml` drops them. `Common/Auth/AuthController.cs` (the old
`/api/auth/login` endpoint) is still physically in the repo and its
comment calls it "retired" — it's flagged, not removed, per your call to
keep this pass scoped to infra.

## Prerequisites — do these first

1. **Azure CLI**, logged in (`az login`), correct subscription selected
   (`az account show`).
2. **Resource providers registered** (one-time per subscription):
   ```
   az provider register --namespace Microsoft.App
   az provider register --namespace Microsoft.ContainerRegistry
   az provider register --namespace Microsoft.Sql
   az provider register --namespace Microsoft.KeyVault
   az provider register --namespace Microsoft.OperationalInsights
   az provider register --namespace Microsoft.ManagedIdentity
   ```
3. **Entra ID app registration** with a client secret already generated and
   tested — you confirmed this is done. Have the client secret value ready;
   `deploy.ps1` will prompt for it (never commit it to a file).
4. **Anthropic API key** ready, if you want document extraction working
   (optional — the app degrades gracefully without it, same as local dev).
5. **A real SMTP relay**, if you want "loan approved" emails to actually
   send. `host.docker.internal` (the local dev trick) doesn't exist in
   Azure — leave `smtpHost` blank to keep emails logged-but-not-sent,
   same graceful degradation as local dev without a mail relay.

## Deploying

```powershell
cd infra
.\deploy.ps1 -ResourceGroup loanplatform-dev -Location eastus
```

This does, in order: creates the resource group; deploys `main.bicep`
(ACR, Key Vault, Azure SQL + 4 databases, Log Analytics, the Container Apps
environment, and all 6 Container Apps — the apps come up unhealthy the
first time through since their images don't exist in ACR yet, that's
expected); builds and pushes all 6 images via `az acr build` (builds
happen in Azure, no local Docker daemon needed); restarts each Container
App's revision so it pulls the now-available image; prints the resulting
URLs.

**EF Core migrations**: `OriginationService/Program.cs` (and presumably
the other three — verify) calls `db.Database.Migrate()` on startup when
`USE_SQL_SERVER=true`, so migrations should apply automatically the first
time each container starts against Azure SQL. This is exactly the EF Core
code path the main README already flags as never having been compiled or
run — watch the container logs on first boot.

**First login check**: hit `https://<origination-url>/api/health`
(should be public, unauthenticated) and then use the UI or the MCP
server's `Login` tool to confirm the Entra ID client-credentials flow
actually works end to end against the deployed app registration.

## Known gaps / good next steps (not done in this pass, scoped out deliberately)

- **SQL networking**: `AllowAzureServices` (0.0.0.0-0.0.0.0, Azure's
  "allow any Azure service" firewall rule) is broad. A private endpoint +
  VNet integration for the Container Apps environment is the hardened
  follow-up.
- **SQL auth**: uses a SQL login/password (via Key Vault), not passwordless
  Azure AD auth, because the EF Core code calls `UseSqlServer()` with a
  plain connection string and has no AAD token provider registered. Adding
  that is a small code change (`Microsoft.Data.SqlClient`'s
  `Authentication=Active Directory Default` + a registered token
  credential) worth doing before this holds real customer data.
- **One shared managed identity** for all 6 Container Apps (ACR pull + Key
  Vault Secrets User). Least-privilege would give each app its own
  identity, scoped to only the secrets it actually uses.
- **MCP server has no access control of its own** (see
  `Services/McpServer/README.md`) — it's deployed internal-ingress-only
  specifically because of this. Don't flip it to external ingress without
  adding an auth layer in front of it first (Container Apps' built-in
  Entra ID "Easy Auth" on the ingress is the least-effort option).
- **Event bus is still the HTTP loopback** (`HttpLoopbackEventBus`) — no
  durability, no retry, an event is lost if a subscriber is down when it's
  published. `Common/EventBus/AzureServiceBusEventBus.reference.cs.txt`
  is a real, unverified reference implementation for swapping in Azure
  Service Bus; `IEventBus` is the only thing application code depends on,
  so that swap changes zero business logic.
- **Image registry**: this uses ACR. Your existing GitHub Actions
  (`.github/workflows/build-service.yml`) still push to GHCR — either
  point CI at ACR too (`az acr login` + `docker push`), or keep GHCR and
  add a registry secret to each Container App instead of the ACR
  identity-based pull. Also note CI currently only builds the 4 backend
  services — the UI and MCP server Dockerfiles aren't wired into
  `.github/workflows/main.yml` yet.
- **Bicep MCP SDK dependency**: `Services/McpServer/Program.cs`'s
  `WithHttpTransport()`/`MapMcp()` calls are written against the
  documented API surface for the pinned `ModelContextProtocol.AspNetCore`
  version — this SDK moves fast (per the original stdio version's own
  README), confirm the method names/routes still match before you build.

## Cost note

Basic-tier ACR, Basic-tier Azure SQL (×4 databases), and Container Apps'
consumption plan are all inexpensive starting points, not production
sizing — expect a low-double-digit-dollars/month footprint for a dev
environment sitting mostly idle. Revisit SKUs before real loan volume.
