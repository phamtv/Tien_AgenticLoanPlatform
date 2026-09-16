// Azure Container Apps deployment for the Loan Platform - Phase B: the
// six Container Apps themselves (origination, underwriting, funding,
// servicing, mcpserver, ui).
//
// Deploy infra/main.bicep FIRST (creates the ACR, Key Vault + secrets,
// shared identity + role assignments, Azure SQL, and the Container Apps
// environment this file references as `existing` - i.e. already deployed,
// not declared here), THEN build and push all six images to that ACR,
// THEN deploy this file. infra/deploy.ps1 orchestrates exactly that order
// automatically - see its comments for why the order matters.
//
// This file takes NO secrets as parameters. Every secret a Container App
// needs (DB connection strings, the Entra ID client secret, SMTP
// credentials, the Anthropic API key) was already written into Key Vault
// by main.bicep; this file only needs each secret's NAME to wire up a
// secretRef, never the raw value.
targetScope = 'resourceGroup'

@description('Must match the namePrefix passed to main.bicep - used to recompute the same resource names, not to create anything new.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'loanplat'

@description('Must match the envName passed to main.bicep, same reason as namePrefix above.')
param envName string = 'dev'

param location string = resourceGroup().location

@description('Entra ID tenant ID — same non-secret value already baked into each service\'s appsettings.json.')
param azureAdTenantId string = 'c9cd754e-fe69-430a-9d84-600a461107f1'

@description('Entra ID app registration client ID — same non-secret value already baked into each service\'s appsettings.json.')
param azureAdClientId string = '8af584e0-5ff0-461d-8ded-14f2676f83f9'

@description('Entra ID API scope the MCP server requests, e.g. api://<clientId>/.default')
param azureAdApiScope string = 'api://8af584e0-5ff0-461d-8ded-14f2676f83f9/.default'

param reviewEmail string = ''
param smtpHost string = ''
param smtpPort string = '587'
param smtpEnableSsl string = 'true'

@description('Image tag to deploy for all six services — set this to your CI build\'s tag (e.g. a git SHA) rather than "latest" for anything beyond a first smoke test.')
param imageTag string = 'latest'

@description('Pass-through of main.bicep\'s anthropicConfigured output — deploy.ps1 reads that output and passes it here via a CLI --parameters override. Controls whether the anthropic-api-key secretRef gets wired into origination-app at all; see main.bicep\'s secretAnthropicApiKey comment for why an unconditional secretRef broke every app referencing an empty-valued Key Vault secret.')
param anthropicConfigured bool = false

@description('Pass-through of main.bicep\'s smtpConfigured output, same mechanism as anthropicConfigured above. Controls whether the smtp-user/smtp-password secretRefs get wired into the four service apps that send email.')
param smtpConfigured bool = false

@description('Opt-in only - defaults to false so a normal deploy.ps1 run never changes this. When true, gives the MCP server a public HTTPS endpoint instead of internal-only, restricted to mcpServerAllowedIp (see that param). Meant for briefly testing an MCP client (e.g. Claude Desktop) against the deployed server from your own machine - see mcpServerApp\'s comment for why this stays internal by default otherwise.')
param mcpServerExternalIngress bool = false

@description('Your machine\'s public IP in CIDR form, e.g. \'203.0.113.7/32\' - required (and only meaningful) when mcpServerExternalIngress is true. Every other IP is denied; get yours from "what is my ip" or whatismyip.com right before deploying, since it can change.')
param mcpServerAllowedIp string = ''

// ---------------------------------------------------------------------
// Naming — must compute identically to main.bicep's formulas, since these
// resolve to resources main.bicep already created.
// ---------------------------------------------------------------------

var acrName = toLower('${namePrefix}acr${envName}')
var keyVaultName = toLower('${namePrefix}-kv-${envName}')
var containerAppsEnvName = '${namePrefix}-cae-${envName}'
var identityName = '${namePrefix}-identity-${envName}'

// ---------------------------------------------------------------------
// References to Phase A's resources — `existing`, not declared/created
// here. Reading their properties (defaultDomain, vaultUri, loginServer)
// works the same as it would for a resource this file created itself.
// ---------------------------------------------------------------------

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvName
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: acrName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// ---------------------------------------------------------------------
// Container Apps — one per service. Image tags assume you've already
// pushed to ACR as loan-platform-<service>:<imageTag> — infra/deploy.ps1
// does this between deploying main.bicep and this file.
// ---------------------------------------------------------------------

var acrLoginServer = acr.properties.loginServer
var envDefaultDomain = containerAppsEnv.properties.defaultDomain
var keyVaultUri = keyVault.properties.vaultUri

// App names computed up front, and internal/external URLs built from them
// directly as plain vars, not module outputs or a func/loop: Container
// Apps' internal FQDN (<app-name>.internal.<environment-default-domain>)
// and external FQDN (<app-name>.<environment-default-domain>) are fully
// deterministic from the app name plus the environment's default domain,
// so there's no need for module outputs (which would create circular
// dependencies between apps that need each other's URLs); and a Bicep
// `func` or `[for ...]` variable loop can't be used here because both
// require values calculable before deployment starts, which
// envDefaultDomain (from containerAppsEnv.properties.defaultDomain, a
// resource property) isn't.
var originationName = '${namePrefix}-origination-${envName}'
var underwritingName = '${namePrefix}-underwriting-${envName}'
var fundingName = '${namePrefix}-funding-${envName}'
var servicingName = '${namePrefix}-servicing-${envName}'
var mcpServerName = '${namePrefix}-mcpserver-${envName}'
var uiName = '${namePrefix}-ui-${envName}'

var originationInternalUrl = 'https://${originationName}.internal.${envDefaultDomain}'
var originationExternalUrl = 'https://${originationName}.${envDefaultDomain}'
var underwritingInternalUrl = 'https://${underwritingName}.internal.${envDefaultDomain}'
var underwritingExternalUrl = 'https://${underwritingName}.${envDefaultDomain}'
var fundingInternalUrl = 'https://${fundingName}.internal.${envDefaultDomain}'
var fundingExternalUrl = 'https://${fundingName}.${envDefaultDomain}'
var servicingInternalUrl = 'https://${servicingName}.internal.${envDefaultDomain}'
var servicingExternalUrl = 'https://${servicingName}.${envDefaultDomain}'
var mcpServerInternalUrl = 'https://${mcpServerName}.internal.${envDefaultDomain}'
var uiExternalUrl = 'https://${uiName}.${envDefaultDomain}'

// Each Container App only gets secretRefs to the Key Vault secrets it
// actually uses. Written as explicit objects rather than a func or a
// `[for ...]` loop, for the same "must be calculable before deployment
// starts" reason as the URLs above.
var smtpUserSecret = { name: 'smtp-user', keyVaultUrl: '${keyVaultUri}secrets/smtp-user', identity: identity.id }
var smtpPasswordSecret = { name: 'smtp-password', keyVaultUrl: '${keyVaultUri}secrets/smtp-password', identity: identity.id }
var underwritingDbSecret = { name: 'underwriting-db-connection-string', keyVaultUrl: '${keyVaultUri}secrets/underwriting-db-connection-string', identity: identity.id }
var fundingDbSecret = { name: 'funding-db-connection-string', keyVaultUrl: '${keyVaultUri}secrets/funding-db-connection-string', identity: identity.id }
var servicingDbSecret = { name: 'servicing-db-connection-string', keyVaultUrl: '${keyVaultUri}secrets/servicing-db-connection-string', identity: identity.id }
var originationDbSecret = { name: 'origination-db-connection-string', keyVaultUrl: '${keyVaultUri}secrets/origination-db-connection-string', identity: identity.id }
var anthropicApiKeySecret = { name: 'anthropic-api-key', keyVaultUrl: '${keyVaultUri}secrets/anthropic-api-key', identity: identity.id }
var azureAdClientSecretRef = { name: 'azuread-client-secret', keyVaultUrl: '${keyVaultUri}secrets/azuread-client-secret', identity: identity.id }

// Only wired up when main.bicep actually created the underlying Key Vault
// secret (i.e. a real, non-empty value was provided) - see main.bicep's
// secretAnthropicApiKey/secretSmtpUser/secretSmtpPassword comment. A
// secretRef pointing at a Key Vault secret that was never created (empty
// value -> conditional resource skipped) would fail exactly like the empty-
// string case did, just with "secret not found" instead of "unable to
// fetch" - so these have to stay in lockstep with main.bicep's conditions.
var emailSecrets = smtpConfigured ? [smtpUserSecret, smtpPasswordSecret] : []
var emailCredEnv = smtpConfigured ? [
  { name: 'Email__Username', secretRef: 'smtp-user' }
  { name: 'Email__Password', secretRef: 'smtp-password' }
] : []
var anthropicSecrets = anthropicConfigured ? [anthropicApiKeySecret] : []
var anthropicEnv = anthropicConfigured ? [
  { name: 'Anthropic__ApiKey', secretRef: 'anthropic-api-key' }
] : []

var underwritingSecrets = concat(emailSecrets, [underwritingDbSecret])
var fundingSecrets = concat(emailSecrets, [fundingDbSecret])
var servicingSecrets = concat(emailSecrets, [servicingDbSecret])
var originationSecrets = concat(emailSecrets, [originationDbSecret], anthropicSecrets)
var mcpSecrets = [azureAdClientSecretRef]

module underwritingApp 'modules/containerapp.bicep' = {
  name: 'underwriting-app'
  params: {
    name: underwritingName
    location: location
    environmentId: containerAppsEnv.id
    environmentDefaultDomain: envDefaultDomain
    identityId: identity.id
    acrLoginServer: acrLoginServer
    image: '${acrLoginServer}/loan-platform-underwritingservice:${imageTag}'
    targetPort: 8080
    externalIngress: true
    secrets: underwritingSecrets
    env: concat([
      { name: 'Email__SmtpHost', value: smtpHost }
      { name: 'Email__SmtpPort', value: smtpPort }
      { name: 'Email__From', value: 'loan-platform@example.com' }
      { name: 'Email__To', value: reviewEmail }
      { name: 'Email__EnableSsl', value: smtpEnableSsl }
    ], emailCredEnv, [
      { name: 'EventSubscribers__UnderwritingDecisionEvent__0', value: fundingInternalUrl }
      { name: 'EventSubscribers__UnderwritingDecisionEvent__1', value: originationInternalUrl }
      { name: 'Cors__AllowedOrigins__0', value: uiExternalUrl }
      { name: 'ConnectionStrings__UnderwritingDb', secretRef: 'underwriting-db-connection-string' }
    ])
  }
}

module fundingApp 'modules/containerapp.bicep' = {
  name: 'funding-app'
  params: {
    name: fundingName
    location: location
    environmentId: containerAppsEnv.id
    environmentDefaultDomain: envDefaultDomain
    identityId: identity.id
    acrLoginServer: acrLoginServer
    image: '${acrLoginServer}/loan-platform-fundingservice:${imageTag}'
    targetPort: 8080
    externalIngress: true
    secrets: fundingSecrets
    env: concat([
      { name: 'Email__SmtpHost', value: smtpHost }
      { name: 'Email__SmtpPort', value: smtpPort }
      { name: 'Email__From', value: 'loan-platform@example.com' }
      { name: 'Email__To', value: reviewEmail }
      { name: 'Email__EnableSsl', value: smtpEnableSsl }
    ], emailCredEnv, [
      { name: 'EventSubscribers__LoanFundedEvent__0', value: servicingInternalUrl }
      { name: 'Cors__AllowedOrigins__0', value: uiExternalUrl }
      { name: 'ConnectionStrings__FundingDb', secretRef: 'funding-db-connection-string' }
    ])
  }
}

module servicingApp 'modules/containerapp.bicep' = {
  name: 'servicing-app'
  params: {
    name: servicingName
    location: location
    environmentId: containerAppsEnv.id
    environmentDefaultDomain: envDefaultDomain
    identityId: identity.id
    acrLoginServer: acrLoginServer
    image: '${acrLoginServer}/loan-platform-servicingservice:${imageTag}'
    targetPort: 8080
    externalIngress: true
    secrets: servicingSecrets
    env: concat([
      { name: 'Email__SmtpHost', value: smtpHost }
      { name: 'Email__SmtpPort', value: smtpPort }
      { name: 'Email__From', value: 'loan-platform@example.com' }
      { name: 'Email__To', value: reviewEmail }
      { name: 'Email__EnableSsl', value: smtpEnableSsl }
    ], emailCredEnv, [
      { name: 'Cors__AllowedOrigins__0', value: uiExternalUrl }
      { name: 'ConnectionStrings__ServicingDb', secretRef: 'servicing-db-connection-string' }
    ])
  }
}

module originationApp 'modules/containerapp.bicep' = {
  name: 'origination-app'
  params: {
    name: originationName
    location: location
    environmentId: containerAppsEnv.id
    environmentDefaultDomain: envDefaultDomain
    identityId: identity.id
    acrLoginServer: acrLoginServer
    image: '${acrLoginServer}/loan-platform-originationservice:${imageTag}'
    targetPort: 8080
    externalIngress: true
    secrets: originationSecrets
    env: concat([
      { name: 'Email__SmtpHost', value: smtpHost }
      { name: 'Email__SmtpPort', value: smtpPort }
      { name: 'Email__From', value: 'loan-platform@example.com' }
      { name: 'Email__To', value: reviewEmail }
      { name: 'Email__EnableSsl', value: smtpEnableSsl }
    ], emailCredEnv, [
      { name: 'EventSubscribers__ApplicationReadyForUnderwritingEvent__0', value: underwritingInternalUrl }
      { name: 'Cors__AllowedOrigins__0', value: uiExternalUrl }
      { name: 'ConnectionStrings__OriginationDb', secretRef: 'origination-db-connection-string' }
    ], anthropicEnv)
  }
}

// MCP server — internal-only by default. It has no access control of its
// own yet (see Services/McpServer/README.md), so exposing it publicly
// means anything that can reach the URL can act as the platform once it
// logs in. Keep this internal (VPN/private access only) until an auth
// layer is added in front of it — see infra/README.md.
//
// mcpServerExternalIngress is the opt-in escape hatch for briefly testing
// an MCP client (Claude Desktop, etc.) against this deployment from your
// own machine: it flips ingress external and locks it to
// mcpServerAllowedIp via ipSecurityRestrictions (allow-list only - every
// other IP gets denied at the platform level, before the request ever
// reaches the container). Both params default to off/empty, so a normal
// `deploy.ps1` run - which never sets them - leaves this internal-only,
// exactly as before.
var mcpIpRestrictions = mcpServerExternalIngress && !empty(mcpServerAllowedIp) ? [
  { name: 'allow-my-ip', ipAddressRange: mcpServerAllowedIp, action: 'Allow' }
] : []

module mcpServerApp 'modules/containerapp.bicep' = {
  name: 'mcpserver-app'
  params: {
    name: mcpServerName
    location: location
    environmentId: containerAppsEnv.id
    environmentDefaultDomain: envDefaultDomain
    identityId: identity.id
    acrLoginServer: acrLoginServer
    image: '${acrLoginServer}/loan-platform-mcpserver:${imageTag}'
    targetPort: 8080
    externalIngress: mcpServerExternalIngress
    ipSecurityRestrictions: mcpIpRestrictions
    healthProbePath: '/health'
    secrets: mcpSecrets
    env: [
      { name: 'Services__Origination', value: originationInternalUrl }
      { name: 'Services__Underwriting', value: underwritingInternalUrl }
      { name: 'Services__Funding', value: fundingInternalUrl }
      { name: 'Services__Servicing', value: servicingInternalUrl }
      { name: 'AzureAd__TenantId', value: azureAdTenantId }
      { name: 'AzureAd__ClientId', value: azureAdClientId }
      { name: 'AzureAd__ApiScope', value: azureAdApiScope }
      { name: 'AzureAd__ClientSecret', secretRef: 'azuread-client-secret' }
    ]
  }
}

module uiApp 'modules/containerapp.bicep' = {
  name: 'ui-app'
  params: {
    name: uiName
    location: location
    environmentId: containerAppsEnv.id
    environmentDefaultDomain: envDefaultDomain
    identityId: identity.id
    acrLoginServer: acrLoginServer
    image: '${acrLoginServer}/loan-platform-ui:${imageTag}'
    targetPort: 80
    externalIngress: true
    healthProbePath: '/'
    secrets: []
    env: [
      // Public HTTPS FQDNs, not internal ones — the browser calls these
      // directly, from outside the Container Apps environment entirely.
      { name: 'ORIGINATION_URL', value: originationExternalUrl }
      { name: 'UNDERWRITING_URL', value: underwritingExternalUrl }
      { name: 'FUNDING_URL', value: fundingExternalUrl }
      { name: 'SERVICING_URL', value: servicingExternalUrl }
    ]
  }
}

// ---------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------

output uiUrl string = uiExternalUrl
output originationUrl string = originationExternalUrl
output underwritingUrl string = underwritingExternalUrl
output fundingUrl string = fundingExternalUrl
output servicingUrl string = servicingExternalUrl
output mcpServerInternalUrl string = mcpServerInternalUrl

@description('Empty unless mcpServerExternalIngress was set true for this deployment - the module\'s own fqdn output already resolves to \'\' when externalIngress is false, so this just adds the https:// prefix when there is one to add.')
output mcpServerExternalUrl string = empty(mcpServerApp.outputs.fqdn) ? '' : 'https://${mcpServerApp.outputs.fqdn}'
