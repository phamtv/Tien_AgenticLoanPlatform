using 'main.bicep'

// Non-secret parameters.
param namePrefix = 'loanplat'
param envName = 'dev'
param sqlAdminLogin = 'loanplatformadmin'

// See main.bicep's sqlLocation description: this is separate from where
// everything else deploys because SQL server creation can be temporarily
// blocked in a given region/subscription combination. If a deploy hits
// "Location X is not accepting creation of new Windows Azure SQL Database
// servers at this time", change this to a different region and re-run.
// Already tried and blocked on this subscription as of 2026-09-14: eastus,
// eastus2 (both "not accepting creation of new Windows Azure SQL Database
// servers at this time" - a real, temporary Azure-side capacity
// restriction, not a Bicep issue). Trying westus2 next.
param sqlLocation = 'westus2'

// Secrets — read from environment variables that deploy.ps1 sets just
// before calling `az deployment group create`, and clears right after.
//
// This is NOT optional styling: a .bicepparam file must assign every
// parameter that has no default value in main.bicep, and Bicep validates
// that *before* any `--parameters name=value` passed alongside it on the
// CLI gets applied. Passing sqlAdminPassword/azureAdClientSecret only as
// CLI overrides (the original approach here) fails with BCP258 ("declared
// in the Bicep file but are missing an assignment in the params file") —
// CLI overrides only work for parameters this file already assigns
// something to, they can't fill one in from scratch. readEnvironmentVariable
// is the supported way to get a value chosen at deploy time into a
// .bicepparam file without hardcoding it here.
param sqlAdminPassword = readEnvironmentVariable('LOANPLATFORM_SQL_ADMIN_PASSWORD')
param azureAdClientSecret = readEnvironmentVariable('LOANPLATFORM_AZUREAD_CLIENT_SECRET')
param anthropicApiKey = readEnvironmentVariable('LOANPLATFORM_ANTHROPIC_API_KEY', '')
