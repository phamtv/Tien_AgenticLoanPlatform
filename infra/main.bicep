// Azure Container Apps deployment for the Loan Platform - Phase A: shared
// infrastructure only (ACR, Key Vault + secrets, Azure SQL, Log Analytics,
// the Container Apps environment, and the shared identity + role
// assignments). The six Container Apps themselves live in apps.bicep
// (Phase B), deployed separately after this and after images are pushed.
//
// Deploy against a resource group (create it first: az group create).
// See infra/README.md for the full step-by-step, including what to run
// before this (az login, provider registration) and after (pushing images,
// running EF Core migrations against Azure SQL).
//
// WHY TWO PHASES: a Container App's ARM deployment fails outright - not
// just "comes up unhealthy" - if the image it references doesn't exist in
// ACR yet, or if its identity's Key Vault/ACR role assignments haven't
// finished propagating through Azure RBAC yet. Both of those are
// guaranteed to be true the first time this deploys if the Container Apps
// are created in the same pass as the ACR/identity/role assignments/Key
// Vault they depend on, before any image has ever been pushed - this was
// tried first and confirmed to fail exactly that way. Splitting into two
// phases - this file, then build+push images, then apps.bicep - gives
// both problems time to resolve themselves before the Container Apps are
// ever created. See infra/deploy.ps1 for the orchestration.
//
// VERIFICATION STATUS: written against the documented Bicep resource
// schemas for Microsoft.App, Microsoft.Sql, Microsoft.KeyVault,
// Microsoft.ContainerRegistry, and Microsoft.OperationalInsights as of
// early 2026, and compiled locally with zero errors as of this revision -
// but Azure SQL server creation can be temporarily blocked per-region per-
// subscription (see sqlLocation below), which is exactly the kind of
// thing that only shows up against a real subscription, not at compile
// time. Validate with `az deployment group what-if` if you want to be
// extra sure before a real run.

targetScope = 'resourceGroup'

@description('Short, globally-unique-ish prefix for resource names, e.g. loanplat. Keep it short — ACR and SQL server names have tight length limits.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'loanplat'

@description('Deployment environment tag, e.g. dev, staging, prod — appended to resource names and used as a tag.')
param envName string = 'dev'

param location string = resourceGroup().location

@description('Azure region for the SQL logical server specifically, kept separate from `location`. Azure SQL server creation is sometimes temporarily blocked in a given region/subscription combination with an error like "Location X is not accepting creation of new Windows Azure SQL Database servers at this time", even when every other resource type in this file deploys to `location` fine. If your deploy hits that error, override this to a different region (e.g. eastus2, centralus, westus2) and re-run — no need to change `location` for everything else.')
param sqlLocation string = 'eastus2'

@description('SQL admin login for the Azure SQL logical server (all four databases share one admin login in this pass — see infra/README.md for the least-privilege follow-up).')
param sqlAdminLogin string = 'loanplatformadmin'

@secure()
@description('SQL admin password. Pass at deploy time; never commit a real value.')
param sqlAdminPassword string

@secure()
@description('Entra ID app registration client secret — used only by the MCP server\'s client-credentials login (Services/McpServer/Tools/AuthTools.cs). Stored in Key Vault here; apps.bicep (Phase B) references it by name via secretRef, it never needs the raw value again after this deploy.')
param azureAdClientSecret string

@secure()
@description('Anthropic API key for the Origination service\'s document-extraction feature. Leave empty to deploy with that feature degraded (matches local dev\'s graceful-degradation behavior) rather than failing the deployment.')
param anthropicApiKey string = ''

@secure()
param smtpUser string = ''

@secure()
param smtpPassword string = ''

// ---------------------------------------------------------------------
// Naming
// ---------------------------------------------------------------------

var acrName = toLower('${namePrefix}acr${envName}')
var sqlServerName = toLower('${namePrefix}-sql-${envName}')
var keyVaultName = toLower('${namePrefix}-kv-${envName}')
var logAnalyticsName = '${namePrefix}-law-${envName}'
var containerAppsEnvName = '${namePrefix}-cae-${envName}'
var identityName = '${namePrefix}-identity-${envName}'

var dbNames = ['OriginationDb', 'UnderwritingDb', 'FundingDb', 'ServicingDb']

var commonTags = {
  application: 'loan-platform'
  environment: envName
}

// ---------------------------------------------------------------------
// Log Analytics + Container Apps environment
// ---------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: commonTags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvName
  location: location
  tags: commonTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ---------------------------------------------------------------------
// Container registry
// ---------------------------------------------------------------------

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: commonTags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false // pulls use the managed identity below, not admin credentials
  }
}

// ---------------------------------------------------------------------
// User-assigned managed identity — shared by all six Container Apps for
// ACR pull + Key Vault secret access. One identity, not six, to keep this
// pass simple; see infra/README.md for the least-privilege follow-up
// (a separate identity per app, each granted only the secrets it needs).
// ---------------------------------------------------------------------

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: commonTags
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, 'AcrPull')
  scope: acr
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull
  }
}

// ---------------------------------------------------------------------
// Key Vault (RBAC authorization — no access policies)
// ---------------------------------------------------------------------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: commonTags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
  }
}

// Whole connection strings (password included), one secret per database —
// not just the bare password — because Container Apps secrets are single
// values referenced by one secretRef per env var; there's no app-side code
// to stitch a value and a separately-referenced password back together.
// EF Core just needs ConnectionStrings:OriginationDb etc. to be one
// complete string, same shape as the local docker-compose value.
resource secretOriginationDbConn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'origination-db-connection-string'
  properties: { value: 'Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=OriginationDb;Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
}

resource secretUnderwritingDbConn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'underwriting-db-connection-string'
  properties: { value: 'Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=UnderwritingDb;Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
}

resource secretFundingDbConn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'funding-db-connection-string'
  properties: { value: 'Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=FundingDb;Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
}

resource secretServicingDbConn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'servicing-db-connection-string'
  properties: { value: 'Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=ServicingDb;Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
}

resource secretAzureAdClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'azuread-client-secret'
  properties: { value: azureAdClientSecret }
}

// Conditional on a real value: Azure Container Apps has a confirmed bug
// (https://github.com/microsoft/azure-container-apps/issues/1291) where a
// Container App fails outright - "Unable to get value using Managed
// identity ... for secret X" - if a secretRef points at a Key Vault secret
// whose value is an empty string, even though the secret exists and RBAC
// is fine. This bit us directly: anthropicApiKey/smtpUser/smtpPassword all
// default to '' (left blank at the deploy.ps1 prompts / never prompted for
// at all, for smtp), so those secrets were being created with an empty
// value and then failing every Container App that referenced them - not
// RBAC propagation lag, which is what that error message misleadingly
// suggests. Fix: only create the secret when there's a real value, and
// surface that as an output so apps.bicep (Phase B) can skip wiring up the
// secretRef/env var entirely when it's not configured - see this file's
// anthropicConfigured/smtpConfigured outputs and apps.bicep's params of
// the same name.
resource secretAnthropicApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(anthropicApiKey)) {
  parent: keyVault
  name: 'anthropic-api-key'
  properties: { value: anthropicApiKey }
}

// Both created (or not) together - partial SMTP credentials aren't usable
// either way, and apps.bicep's smtpConfigured flag is a single bool
// covering both.
resource secretSmtpUser 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(smtpUser) && !empty(smtpPassword)) {
  parent: keyVault
  name: 'smtp-user'
  properties: { value: smtpUser }
}

resource secretSmtpPassword 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(smtpUser) && !empty(smtpPassword)) {
  parent: keyVault
  name: 'smtp-password'
  properties: { value: smtpPassword }
}

// ---------------------------------------------------------------------
// Azure SQL — one logical server, four databases (one per service,
// matching the current SQL Server container's OriginationDb/
// UnderwritingDb/FundingDb/ServicingDb). SQL login/password auth, since
// the EF Core code (OriginationDbContext etc.) calls UseSqlServer() with
// a plain connection string and has no AAD token provider wired in — see
// infra/README.md for the passwordless/managed-identity DB auth
// follow-up. Deployed to `sqlLocation`, not `location` — see that
// parameter's description above.
// ---------------------------------------------------------------------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: sqlLocation
  tags: commonTags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }
}

// Lets Container Apps (no VNet integration in this pass) reach Azure SQL.
// This is the broad, simple option — see infra/README.md for the private
// endpoint / VNet-integrated follow-up before treating this as production-
// hardened.
resource sqlAllowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabases 'Microsoft.Sql/servers/databases@2023-08-01-preview' = [for dbName in dbNames: {
  parent: sqlServer
  name: dbName
  location: sqlLocation
  tags: commonTags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    maxSizeBytes: 2147483648 // 2 GB — Basic tier ceiling; bump the SKU before real loan volume
  }
}]

// ---------------------------------------------------------------------
// Outputs — consumed by infra/deploy.ps1: acrLoginServerOut to build and
// push images, the rest just surfaced for convenience. Container App URLs
// are NOT outputs of this file — see apps.bicep (Phase B) for those,
// since the Container Apps don't exist yet at the end of this deployment.
// ---------------------------------------------------------------------

output acrLoginServerOut string = acr.properties.loginServer
output sqlServerFqdn string = '${sqlServerName}.database.windows.net'
output keyVaultUri string = keyVault.properties.vaultUri

// Tells apps.bicep (Phase B) whether it's safe to wire up the
// anthropic-api-key / smtp-user+smtp-password secretRefs - see the
// secretAnthropicApiKey/secretSmtpUser/secretSmtpPassword comment above.
// The linter's outputs-should-not-contain-secrets rule flags these below
// as a false positive - it's reacting to the secure params being
// referenced at all, but !empty(...) only ever produces a bool (whether a
// value was provided), never the secret value itself.
#disable-next-line outputs-should-not-contain-secrets
output anthropicConfigured bool = !empty(anthropicApiKey)
#disable-next-line outputs-should-not-contain-secrets
output smtpConfigured bool = !empty(smtpUser) && !empty(smtpPassword)
