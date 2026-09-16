<#
.SYNOPSIS
  One-time setup for the CD pipeline in .github/workflows/main.yml and
  build-service.yml: registers an Azure AD app for GitHub Actions to use
  via OIDC (no client secret is ever created or stored), grants it just
  enough access (AcrPush on your ACR, Container Apps Contributor on your
  resource group), and wires the GitHub repo secrets/variables the
  workflows read.

.NOTES
  Safe to re-run — every step checks what already exists before creating
  anything, so if it fails partway through, just fix the issue and run it
  again.

  Requires: Azure CLI (az), logged in as a user with permission to create
  app registrations and role assignments (typically Owner or a
  combination of Application Administrator + User Access Administrator).
  Optional: GitHub CLI (gh), logged in — if present, this script sets the
  GitHub secrets/variables for you; otherwise it prints the values to add
  manually.
#>

$ErrorActionPreference = "Stop"

# --- Override these if auto-discovery below picks the wrong one ---
$ResourceGroup = $null   # e.g. "loanplatform-dev" — leave $null to auto-detect
$GitHubRepo    = $null   # e.g. "tienvpham/Tien_LoanPlatform" — leave $null to auto-detect

Write-Host "=== 1. Confirming Azure CLI login ===" -ForegroundColor Cyan
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in - launching az login..." -ForegroundColor Yellow
    az login | Out-Null
    $account = az account show | ConvertFrom-Json
}
$SubscriptionId = $account.id
$TenantId = $account.tenantId
Write-Host "Logged in as $($account.user.name)"
Write-Host "Subscription: $($account.name) ($SubscriptionId)"
Write-Host "Tenant: $TenantId`n"

Write-Host "=== 2. Finding your resource group ===" -ForegroundColor Cyan
if (-not $ResourceGroup) {
    $rgList = @(az group list --query "[?starts_with(name, 'loanplat')].name" -o tsv) -split "`n" | Where-Object { $_ -ne "" }
    if ($rgList.Count -eq 0) {
        throw "No resource group found starting with 'loanplat'. Set `$ResourceGroup near the top of this script and re-run."
    } elseif ($rgList.Count -gt 1) {
        Write-Host "Multiple matching resource groups found:"
        $rgList | ForEach-Object { Write-Host "  - $_" }
        throw "Set `$ResourceGroup near the top of this script to the one you want and re-run."
    } else {
        $ResourceGroup = $rgList[0]
    }
}
Write-Host "Resource group: $ResourceGroup`n"

Write-Host "=== 3. Finding your Container Registry ===" -ForegroundColor Cyan
$AcrName = az acr list --resource-group $ResourceGroup --query "[0].name" -o tsv
if (-not $AcrName) { throw "No Azure Container Registry found in resource group $ResourceGroup." }
$AcrId = az acr show --name $AcrName --query id -o tsv
Write-Host "ACR: $AcrName`n"

Write-Host "=== 4. Environment name ===" -ForegroundColor Cyan
# Matches infra/main.bicepparam's envName. Change here if you deployed a
# different environment than "dev".
$EnvName = "dev"
Write-Host "Environment: $EnvName`n"

Write-Host "=== 5. Finding your GitHub repo ===" -ForegroundColor Cyan
if (-not $GitHubRepo) {
    $remoteUrl = git remote get-url origin 2>$null
    if (-not $remoteUrl) {
        throw "Couldn't read 'git remote origin' (are you running this from inside the repo?). Set `$GitHubRepo (e.g. 'owner/repo') near the top of this script and re-run."
    }
    if ($remoteUrl -match "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(\.git)?/?$") {
        $GitHubRepo = "$($Matches.owner)/$($Matches.repo)"
    } else {
        throw "Couldn't parse a GitHub owner/repo out of remote URL '$remoteUrl'. Set `$GitHubRepo manually near the top of this script and re-run."
    }
}
Write-Host "GitHub repo: $GitHubRepo`n"

Write-Host "=== 6. Creating (or reusing) the Azure AD app registration ===" -ForegroundColor Cyan
$AppName = "loanplat-github-actions-$EnvName"
$existingApp = az ad app list --display-name $AppName --query "[0]" -o json | ConvertFrom-Json
if ($existingApp) {
    $AppId = $existingApp.appId
    Write-Host "Reusing existing app registration '$AppName' (appId $AppId)"
} else {
    $newApp = az ad app create --display-name $AppName | ConvertFrom-Json
    $AppId = $newApp.appId
    Write-Host "Created app registration '$AppName' (appId $AppId)"
}
Write-Host ""

Write-Host "=== 7. Creating (or reusing) the service principal ===" -ForegroundColor Cyan
$existingSp = az ad sp show --id $AppId 2>$null | ConvertFrom-Json
if (-not $existingSp) {
    az ad sp create --id $AppId | Out-Null
    Write-Host "Service principal created"
} else {
    Write-Host "Service principal already exists"
}
Write-Host ""

Write-Host "=== 8. Federated credential (trusts GitHub Actions on main - no client secret) ===" -ForegroundColor Cyan
$credName = "github-main"
$existingCreds = az ad app federated-credential list --id $AppId | ConvertFrom-Json
$alreadyHasCred = $existingCreds | Where-Object { $_.name -eq $credName }
if ($alreadyHasCred) {
    Write-Host "Federated credential '$credName' already exists"
} else {
    $credFile = New-TemporaryFile
    @{
        name        = $credName
        issuer      = "https://token.actions.githubusercontent.com"
        subject     = "repo:${GitHubRepo}:ref:refs/heads/main"
        description = "Loan Platform CD - push to main"
        audiences   = @("api://AzureADTokenExchange")
    } | ConvertTo-Json | Set-Content -Path $credFile.FullName -Encoding utf8

    az ad app federated-credential create --id $AppId --parameters "@$($credFile.FullName)" | Out-Null
    Remove-Item $credFile.FullName
    Write-Host "Federated credential created (subject: repo:${GitHubRepo}:ref:refs/heads/main)"
}
Write-Host ""

Write-Host "=== 9. Role assignments ===" -ForegroundColor Cyan
$rgId = az group show --name $ResourceGroup --query id -o tsv

function Ensure-RoleAssignment {
    param($Scope, $Role, $Label)
    $existing = az role assignment list --assignee $AppId --scope $Scope --query "[?roleDefinitionName=='$Role']" -o tsv
    if ($existing) {
        Write-Host "$Label already has '$Role'"
    } else {
        az role assignment create --assignee $AppId --role $Role --scope $Scope | Out-Null
        Write-Host "Granted '$Role' on $Label"
    }
}
Ensure-RoleAssignment -Scope $AcrId -Role "AcrPush" -Label "ACR ($AcrName)"
Ensure-RoleAssignment -Scope $rgId -Role "Container Apps Contributor" -Label "resource group ($ResourceGroup)"
Write-Host ""

Write-Host "=== 10. GitHub secrets & variables ===" -ForegroundColor Cyan
$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
$ghReady = $false
if ($ghCmd) {
    $ghAuthed = gh auth status 2>&1 | Select-String "Logged in"
    if ($ghAuthed) { $ghReady = $true }
}

if ($ghReady) {
    gh secret set AZURE_CLIENT_ID --repo $GitHubRepo --body $AppId
    gh secret set AZURE_TENANT_ID --repo $GitHubRepo --body $TenantId
    gh secret set AZURE_SUBSCRIPTION_ID --repo $GitHubRepo --body $SubscriptionId
    gh variable set AZURE_RESOURCE_GROUP --repo $GitHubRepo --body $ResourceGroup
    gh variable set AZURE_ENV_NAME --repo $GitHubRepo --body $EnvName
    Write-Host "GitHub secrets and variables set via gh CLI." -ForegroundColor Green
} else {
    Write-Host "gh CLI not found/authenticated - add these manually at:" -ForegroundColor Yellow
    Write-Host "  https://github.com/$GitHubRepo/settings/secrets/actions`n"
    Write-Host "Repository secrets (Secrets tab):"
    Write-Host "  AZURE_CLIENT_ID       = $AppId"
    Write-Host "  AZURE_TENANT_ID       = $TenantId"
    Write-Host "  AZURE_SUBSCRIPTION_ID = $SubscriptionId"
    Write-Host "`nRepository variables (Variables tab, same page):"
    Write-Host "  AZURE_RESOURCE_GROUP  = $ResourceGroup"
    Write-Host "  AZURE_ENV_NAME        = $EnvName"
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
Write-Host "Push a change to main and watch it deploy: https://github.com/$GitHubRepo/actions"
