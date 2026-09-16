using 'apps.bicep'

// No secrets here — apps.bicep only needs Key Vault secret NAMES (wired
// up as secretRef), and it looks up Key Vault itself as an `existing`
// resource. Every value below is non-secret.
param namePrefix = 'loanplat'
param envName = 'dev'
param azureAdApiScope = 'api://8af584e0-5ff0-461d-8ded-14f2676f83f9/.default'
param reviewEmail = ''
param smtpHost = ''
param smtpPort = '587'
param smtpEnableSsl = 'true'
param imageTag = 'latest'
