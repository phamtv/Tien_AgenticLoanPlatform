// Generic Container App module, reused once per service (origination,
// underwriting, funding, servicing, mcpserver, ui) from main.bicep.
// Each caller builds its own fully-shaped `env` array (mixing plain
// values and Key Vault secretRefs as needed) — this module doesn't try to
// guess which env vars are secret, it just passes through what it's given.

@description('Container App name, e.g. loanplatform-origination')
param name string

@description('Location for all resources')
param location string = resourceGroup().location

@description('Resource ID of the Container Apps managed environment')
param environmentId string

@description('The managed environment\'s default domain (its properties.defaultDomain) — used to build this app\'s internal FQDN reliably, regardless of whether it has external ingress')
param environmentDefaultDomain string

@description('Resource ID of the user-assigned managed identity used for ACR pull + Key Vault access')
param identityId string

@description('Full image reference, e.g. myregistry.azurecr.io/loan-platform-origination:latest')
param image string

@description('ACR login server, e.g. myregistry.azurecr.io — used to configure the registry credential')
param acrLoginServer string

@description('Port the container listens on (matches ASPNETCORE_URLS / nginx EXPOSE)')
param targetPort int

@description('Whether this app gets a public HTTPS endpoint. false = reachable only from inside the Container Apps environment (other apps use the internal FQDN).')
param externalIngress bool = true

@description('Optional IP allow-list for external ingress, e.g. [{ name: \'my-ip\', ipAddressRange: \'1.2.3.4/32\', action: \'Allow\' }]. Ignored when externalIngress is false. Leave empty (the default) for no restriction - anyone can reach the endpoint once it is external. Container Apps treats a non-empty list as allow-list-only: any IP not explicitly listed is denied, which is what makes this safe to use for a normally-internal app you want to temporarily open to just your own machine.')
param ipSecurityRestrictions array = []

@description('Fully-shaped env var array: [{ name: string, value: string }] or [{ name: string, secretRef: string }]')
param env array = []

@description('Container Apps-level secrets: [{ name: string, keyVaultUrl: string, identity: string }] for Key Vault-backed secrets')
param secrets array = []

@description('HTTP path for liveness/readiness probes')
param healthProbePath string = '/api/health'

@description('Min/max replica count')
param minReplicas int = 1
param maxReplicas int = 3

@description('CPU cores and memory per replica — small services, small footprint')
param cpu string = '0.5'
param memory string = '1Gi'

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: externalIngress
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        ipSecurityRestrictions: empty(ipSecurityRestrictions) ? null : ipSecurityRestrictions
      }
      registries: [
        {
          server: acrLoginServer
          identity: identityId
        }
      ]
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: env
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: healthProbePath
                port: targetPort
              }
              initialDelaySeconds: 10
              periodSeconds: 15
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: healthProbePath
                port: targetPort
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

@description('Public HTTPS FQDN if external, otherwise empty')
output fqdn string = externalIngress ? containerApp.properties.configuration.ingress.fqdn : ''

@description('Internal FQDN — reachable from other apps in the same Container Apps environment, regardless of the external setting above. This is what service-to-service calls (event subscribers, the MCP server calling the four APIs) should use. main.bicep actually computes this itself from the app name rather than reading this output, to avoid a circular module dependency — this is kept as a convenience output for anything deployed separately later.')
output internalFqdn string = '${name}.internal.${environmentDefaultDomain}'
