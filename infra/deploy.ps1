# Deploys the Loan Platform to Azure Container Apps.
#
# TWO-PHASE DEPLOY: main.bicep (Phase A - ACR, Key Vault, SQL, identity,
# environment) deploys first, then all six images get built and pushed to
# that ACR, THEN apps.bicep (Phase B - the six Container Apps) deploys.
# This split exists because a Container App's ARM deployment fails
# outright - not just "comes up unhealthy" - if its image doesn't exist in
# ACR yet, or if its identity's Key Vault/ACR role assignments haven't
# finished propagating through Azure RBAC yet. Creating the Container Apps
# in the same pass as the infrastructure they depend on, before any image
# is pushed, was tried first and confirmed to fail exactly that way -
# see infra/main.bicep's and infra/apps.bicep's header comments.
#
# VERIFICATION STATUS: main.bicep and apps.bicep have each been compiled
# locally with zero errors as of this revision, but a full run against a
# real subscription can still surface things a compiler can't catch (for
# example: Azure SQL server creation being temporarily blocked in a given
# region - see main.bicep's sqlLocation parameter if that happens to you).
# Read this script before running it, especially the secret prompts.
#
# Prerequisites (see infra/README.md for detail):
#   - Azure CLI installed and logged in (az login), correct subscription
#     selected (az account show)
#   - Resource providers registered: Microsoft.App, Microsoft.ContainerRegistry,
#     Microsoft.Sql, Microsoft.KeyVault, Microsoft.OperationalInsights,
#     Microsoft.ManagedIdentity
#   - An Entra ID app registration with a client secret already generated
#     (you confirmed this is already set up and tested)
#
# Usage:
#   .\deploy.ps1 -ResourceGroup loanplatform-dev -Location eastus

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [string]$Location = "eastus",

    [string]$EnvName = "dev"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."

Write-Host "== Loan Platform -> Azure Container Apps deploy ==" -ForegroundColor Cyan
Write-Host "Resource group: $ResourceGroup  Location: $Location  Env: $EnvName`n"

# --- Step 0: confirm login -------------------------------------------------
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Error "Not logged in. Run 'az login' first."
    exit 1
}
Write-Host "Logged in as $($account.user.name), subscription $($account.name)"

# --- Step 1: secrets, prompted (never hardcode these) ----------------------
$sqlAdminPassword    = Read-Host "SQL admin password (new, will be created)" -AsSecureString
$azureAdClientSecret = Read-Host "Entra ID app registration client secret" -AsSecureString
$anthropicApiKey     = Read-Host "Anthropic API key (leave blank to disable document extraction)" -AsSecureString

function ConvertFrom-SecureStringPlain($secure) {
    [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

# --- Step 2: resource group --------------------------------------------------
Write-Host "`n== Creating resource group (if needed) ==" -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location | Out-Null

# Shared: fetches and prints the detailed Azure-side error(s) for ONE
# specific deployment name (e.g. "main" or "apps" - always the deployment
# this script just attempted), since the top-level az deployment group
# create failure text is usually just a generic wrapper ("At least one
# resource deployment operation failed...") and the actual reason is one
# level down.
#
# Deliberately scoped to a single deployment name, not "every currently-
# Failed deployment in the resource group" (an earlier version of this
# function did that): Azure's deployment history for reused nested-
# deployment names (the module names in main.bicep/apps.bicep, like
# "origination-app") doesn't necessarily clear cleanly between runs, so a
# broad query mixes in stale results from earlier attempts alongside the
# real, current failure - confirmed the hard way while building this
# script. Passing an explicit $DeploymentName keeps the output limited to
# what this specific call actually just did.
function Show-DeploymentErrors {
    param([string]$DeploymentName)
    try {
        $ops = az deployment operation group list --resource-group $ResourceGroup --name $DeploymentName 2>$null | ConvertFrom-Json
        foreach ($op in $ops) {
            if ($op.properties.provisioningState -eq 'Failed' -and $op.properties.statusMessage) {
                $target = $op.properties.targetResource.id
                if ($target) { Write-Host "`n-- $target --" -ForegroundColor Yellow }
                $msg = $op.properties.statusMessage
                if ($msg -is [string]) { Write-Host $msg } else { Write-Host ($msg | ConvertTo-Json -Depth 10) }
            }
        }
    } catch {
        Write-Host "(Could not fetch detailed errors automatically - $_)"
    }
}

# --- Step 3: Phase A - shared infrastructure (main.bicep) --------------------
# Creates ACR, Key Vault + secrets, SQL, Log Analytics, the Container Apps
# environment, and the shared identity + role assignments. No Container
# Apps yet - those are Phase B (Step 5), after images exist to reference.
#
# Secrets go in via environment variables, not `--parameters name=value` on
# the CLI. A .bicepparam file must assign every parameter that has no
# default in main.bicep, and Bicep checks that before any CLI override is
# applied - so a CLI override can't fill in a value this file never
# assigned in the first place, it can only replace one that's already
# there. main.bicepparam reads these three via readEnvironmentVariable(),
# which is why they're set here first and cleared right after the deploy
# call, win or lose.
$env:LOANPLATFORM_SQL_ADMIN_PASSWORD    = ConvertFrom-SecureStringPlain $sqlAdminPassword
$env:LOANPLATFORM_AZUREAD_CLIENT_SECRET = ConvertFrom-SecureStringPlain $azureAdClientSecret
$env:LOANPLATFORM_ANTHROPIC_API_KEY     = ConvertFrom-SecureStringPlain $anthropicApiKey

Write-Host "`n== Deploying shared infrastructure (main.bicep - Phase A) ==" -ForegroundColor Cyan
$infraOutput = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$RepoRoot\infra\main.bicep" `
    --parameters "$RepoRoot\infra\main.bicepparam" `
    --parameters envName=$EnvName `
    | ConvertFrom-Json

Remove-Item Env:\LOANPLATFORM_SQL_ADMIN_PASSWORD -ErrorAction SilentlyContinue
Remove-Item Env:\LOANPLATFORM_AZUREAD_CLIENT_SECRET -ErrorAction SilentlyContinue
Remove-Item Env:\LOANPLATFORM_ANTHROPIC_API_KEY -ErrorAction SilentlyContinue

# `az` is a native command, not a PowerShell cmdlet - a failed `az`
# invocation does NOT trigger $ErrorActionPreference = "Stop" on its own,
# and a failure can still leave $infraOutput as a parseable-but-empty
# object rather than $null. Without this explicit check, a failed
# deployment here would silently cascade into broken, registry-less image
# references several steps later instead of a clear error right where the
# real problem is.
if ($LASTEXITCODE -ne 0 -or -not $infraOutput -or -not $infraOutput.properties.outputs.acrLoginServerOut.value) {
    Write-Host "`n== Phase A deployment failed - fetching the detailed error(s) ==" -ForegroundColor Red
    Show-DeploymentErrors -DeploymentName "main"
    Write-Error "main.bicep (Phase A) deployment failed (see the detailed error(s) printed just above, and/or any 'az' error text further up the console). Common causes: a resource provider isn't registered - see this script's Prerequisites comment near the top, or infra/README.md; or Azure SQL server creation is temporarily blocked in the region you passed via -Location - if the error above says something like 'Location ... is not accepting creation of new Windows Azure SQL Database servers at this time', edit infra/main.bicepparam's sqlLocation value to a different region (e.g. eastus2, centralus, westus2) and re-run this script from the top. Otherwise, fix the specific issue shown above, then re-run."
    exit 1
}

$acrLoginServer = "$($infraOutput.properties.outputs.acrLoginServerOut.value)"
$acrName = $acrLoginServer.Split('.')[0]
Write-Host "ACR: $acrName ($acrLoginServer)"

# Whether main.bicep actually created the anthropic-api-key / smtp-user +
# smtp-password Key Vault secrets (it skips them when left blank - see
# main.bicep's secretAnthropicApiKey comment). apps.bicep (Phase B) needs
# to know this so it can skip wiring up those secretRefs entirely rather
# than pointing at a secret that doesn't exist, or - the original bug this
# fixes - one that exists but is empty, which Container Apps fails to read
# every time (a confirmed Azure bug, not RBAC lag:
# https://github.com/microsoft/azure-container-apps/issues/1291).
# ConvertFrom-Json turns these into real booleans; az's --parameters needs
# lowercase "true"/"false" text, hence the ToString().ToLower().
$anthropicConfigured = "$($infraOutput.properties.outputs.anthropicConfigured.value)".ToLower()
$smtpConfigured = "$($infraOutput.properties.outputs.smtpConfigured.value)".ToLower()
Write-Host "Anthropic doc-extraction secret configured: $anthropicConfigured   SMTP email secrets configured: $smtpConfigured"

# --- Step 3.5: stage a clean build context ------------------------------------
# `az acr build` uploads the build context by packing it into a local tar
# BEFORE handing anything to Azure, and that packing step only skips .git
# and .gitignore by hardcoded default - confirmed the hard way that it does
# NOT apply .dockerignore's own exclusions (bin/, obj/, .vs/, *.log), even
# though this repo's .dockerignore already lists all of those correctly.
# Left pointed straight at $RepoRoot, that packing step tries to archive
# .vs\...\*.vsidx - a search-index file Visual Studio holds open while
# running - and fails with a Windows permission-denied error.
#
# Fix: robocopy a clean copy of the repo into a scratch folder first,
# skipping .git/.vs/bin/obj/node_modules (and .env, so a stray secret file
# never ends up inside an image build context), and build from THAT copy
# instead of the live, VS-locked repo. This sidesteps the ignore-pattern gap
# entirely - nothing excluded ever reaches the tar because it was never
# copied - and as a side benefit noticeably shrinks/speeds up every upload.
Write-Host "`n== Staging a clean build context (skips .git/.vs/bin/obj) ==" -ForegroundColor Cyan
$BuildContext = Join-Path $env:TEMP "loanplatform-build-context"
if (Test-Path $BuildContext) {
    Remove-Item $BuildContext -Recurse -Force -ErrorAction SilentlyContinue
}
robocopy "$RepoRoot" "$BuildContext" /MIR `
    /XD ".git" ".vs" "bin" "obj" "node_modules" "Claude outputs" `
    /XF "*.user" ".env" `
    /NFL /NDL /NJH /NJS /NP | Out-Null
# Robocopy's exit codes are not the usual 0-means-success: 0-7 all indicate
# normal outcomes (files copied, some skipped, etc.), only 8+ is a real
# failure.
if ($LASTEXITCODE -ge 8) {
    Write-Error "robocopy failed while staging a clean build context (exit code $LASTEXITCODE - codes 8 and above indicate a real error; 0-7 are normal). Check that $RepoRoot is accessible and re-run."
    exit 1
}
Write-Host "Build context ready at $BuildContext"

# --- Step 4: build & push all six images via ACR Tasks -----------------------
# az acr build uploads your source and builds INSIDE Azure - no local Docker
# daemon required. Each Dockerfile's context is $BuildContext (the clean
# staged copy from Step 3.5), not the live repo - see that step's comment.
Write-Host "`n== Building & pushing images (ACR Tasks) ==" -ForegroundColor Cyan

$services = @(
    @{ Name = "originationservice";  Dockerfile = "Services/OriginationService/Dockerfile" },
    @{ Name = "underwritingservice"; Dockerfile = "Services/UnderwritingService/Dockerfile" },
    @{ Name = "fundingservice";      Dockerfile = "Services/FundingService/Dockerfile" },
    @{ Name = "servicingservice";    Dockerfile = "Services/ServicingService/Dockerfile" },
    @{ Name = "mcpserver";           Dockerfile = "Services/McpServer/Dockerfile" }
)

foreach ($svc in $services) {
    $image = "loan-platform-$($svc.Name):latest"
    Write-Host "`n-- Building $image --"
    az acr build --registry $acrName --image $image --file "$BuildContext\$($svc.Dockerfile)" "$BuildContext" --build-arg USE_SQL_SERVER=true
}

Write-Host "`n-- Building loan-platform-ui:latest --"
az acr build --registry $acrName --image "loan-platform-ui:latest" --file "$BuildContext\ui\Dockerfile" "$BuildContext\ui"

# --- Step 5: Phase B - the six Container Apps (apps.bicep) -------------------
# Only now do the images this references actually exist in ACR. No secrets
# are passed here at all - apps.bicep looks up Key Vault itself and only
# needs secret NAMES to build secretRefs.
#
# Retried up to 3 times as extra insurance: apps.bicep can't declare a
# dependsOn on main.bicep's role assignments (they're not resources this
# file creates, just referenced as `existing`), and while the several
# minutes spent building six images in Step 4 should be more than enough
# for Key Vault/ACR RBAC to have finished propagating from Phase A, this
# retry loop is a cheap safety net in case it somehow hasn't. Re-running
# the same idempotent deployment after a short wait only retries what
# actually failed.
$maxAttempts = 3
$appsOutput = $null
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    if ($attempt -gt 1) {
        Write-Host "`n-- Retry $attempt of ${maxAttempts} - waiting 60s first --" -ForegroundColor Yellow
        Start-Sleep -Seconds 60
    }

    Write-Host "`n== Deploying Container Apps (apps.bicep - Phase B - attempt $attempt of $maxAttempts) ==" -ForegroundColor Cyan
    $appsOutput = az deployment group create `
        --resource-group $ResourceGroup `
        --template-file "$RepoRoot\infra\apps.bicep" `
        --parameters "$RepoRoot\infra\apps.bicepparam" `
        --parameters envName=$EnvName anthropicConfigured=$anthropicConfigured smtpConfigured=$smtpConfigured `
        | ConvertFrom-Json

    if ($LASTEXITCODE -eq 0 -and $appsOutput -and $appsOutput.properties.outputs.uiUrl.value) {
        break
    }
}

if ($LASTEXITCODE -ne 0 -or -not $appsOutput -or -not $appsOutput.properties.outputs.uiUrl.value) {
    Write-Host "`n== Phase B deployment failed - fetching the detailed error(s) ==" -ForegroundColor Red
    Show-DeploymentErrors -DeploymentName "apps"
    Write-Error "apps.bicep (Phase B) deployment failed (see the detailed error(s) printed just above, and/or any 'az' error text further up the console). If the error mentions 'Unable to get value using Managed identity' / fetching a secret, that's Azure RBAC role-assignment propagation lag - wait a few minutes and re-run this script from the top (main.bicep and the image builds are idempotent, they won't redo work that already succeeded). If it mentions MANIFEST_UNKNOWN / an image tag not found, double check Step 4 above actually pushed all six images successfully. Otherwise, fix the specific issue shown above, then re-run."
    exit 1
}

# --- Step 6: force each Container App to pull the newly-pushed image ---------
# Redundant on a brand-new resource group (apps.bicep just created every
# app with the correct image), but this makes re-running the whole script
# later - after rebuilding images with the same :latest tag - actually
# pick up the new build. `az containerapp update --image ...` with an
# explicit, guaranteed-new --revision-suffix forces a brand new revision
# every time, which always re-pulls, regardless of whether the image tag
# string itself looks unchanged. (`az containerapp revision restart` was
# tried first and doesn't work for this - it needs a specific --revision
# name, not just the app name, and even then a plain restart doesn't
# reliably re-pull a `:latest` tag.)
Write-Host "`n== Updating Container Apps to a new revision (forces a fresh image pull) ==" -ForegroundColor Cyan
$revisionSuffix = "deploy$(Get-Date -Format yyyyMMddHHmmss)"

$appImageMap = @(
    @{ App = "origination";  Image = "loan-platform-originationservice:latest" },
    @{ App = "underwriting"; Image = "loan-platform-underwritingservice:latest" },
    @{ App = "funding";      Image = "loan-platform-fundingservice:latest" },
    @{ App = "servicing";    Image = "loan-platform-servicingservice:latest" },
    @{ App = "mcpserver";    Image = "loan-platform-mcpserver:latest" },
    @{ App = "ui";           Image = "loan-platform-ui:latest" }
)

foreach ($entry in $appImageMap) {
    $appName = "loanplat-$($entry.App)-$EnvName"
    $fullImage = "$acrLoginServer/$($entry.Image)"
    Write-Host "-- $appName -> $fullImage (revision suffix: $revisionSuffix) --"
    az containerapp update --name $appName --resource-group $ResourceGroup --image $fullImage --revision-suffix $revisionSuffix
}

# --- Step 7: show URLs --------------------------------------------------------
Write-Host "`n== Done ==" -ForegroundColor Green
Write-Host "UI:            $($appsOutput.properties.outputs.uiUrl.value)"
Write-Host "Origination:   $($appsOutput.properties.outputs.originationUrl.value)"
Write-Host "Underwriting:  $($appsOutput.properties.outputs.underwritingUrl.value)"
Write-Host "Funding:       $($appsOutput.properties.outputs.fundingUrl.value)"
Write-Host "Servicing:     $($appsOutput.properties.outputs.servicingUrl.value)"
Write-Host "MCP (internal only): $($appsOutput.properties.outputs.mcpServerInternalUrl.value)"
Write-Host "`nCheck each service's /api/health endpoint and the Container Apps logs in the portal if anything looks unhealthy."
