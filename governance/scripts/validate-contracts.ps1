<#
.SYNOPSIS
    Validates that governance contract files are not older than the source files they document.
.DESCRIPTION
    Compares last-write timestamps of source files vs governance contracts.
    Exit 0 = all fresh, 1 = stale, 2 = error.
.PARAMETER Quiet
    Suppress per-file output; only print summary.
.PARAMETER FixHint
    Show which source files caused each stale contract.
.EXAMPLE
    & governance/scripts/validate-contracts.ps1
#>

param([switch]$Quiet, [switch]$FixHint)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))

# ── Helpers
function Write-Pass($msg) { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "  ✗ $msg" -ForegroundColor Red }
function Write-Warn($msg) { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Header($msg) { Write-Host "`n── $msg ──" -ForegroundColor Cyan }

$script:stale = 0
$script:fresh = 0
$script:missing = 0

function Get-NewestFile($Paths) {
    $newest = $null
    foreach ($p in $Paths) {
        $full = Join-Path $root $p
        if (Test-Path $full) {
            $item = Get-Item $full
            if ((-not $newest) -or ($item.LastWriteTime -gt $newest.LastWriteTime)) {
                $newest = $item
            }
        }
    }
    return $newest
}

function Test-Contract($ContractRelPath, $SourceRelPaths, $Description) {
    $contractPath = Join-Path $root $ContractRelPath
    if (-not (Test-Path $contractPath)) {
        if (-not $Quiet) { Write-Fail "$Description - CONTRACT MISSING: $ContractRelPath" }
        $script:missing = $script:missing + 1
        return
    }
    $contract = Get-Item $contractPath
    $newestSource = Get-NewestFile $SourceRelPaths
    if (-not $newestSource) {
        if (-not $Quiet) { Write-Warn "$Description - no source files found to compare" }
        return
    }
    if ($contract.LastWriteTime -lt $newestSource.LastWriteTime) {
        if (-not $Quiet) {
            Write-Fail "$Description - STALE (contract older than source)"
            Write-Host "    Contract : $ContractRelPath ($($contract.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"
            Write-Host "    Newest source: $($newestSource.Name) ($($newestSource.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"
        }
        if ($FixHint) {
            Write-Host "    Source files to check:"
            foreach ($s in $SourceRelPaths) {
                $full = Join-Path $root $s
                if (Test-Path $full) {
                    $item = Get-Item $full
                    if ($item.LastWriteTime -gt $contract.LastWriteTime) {
                        Write-Host "       $s (modified $($item.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))" -ForegroundColor DarkYellow
                    }
                }
            }
        }
        $script:stale = $script:stale + 1
    }
    else {
        if (-not $Quiet) { Write-Pass "$Description" }
        $script:fresh = $script:fresh + 1
    }
}

# ═══════════════════════════════════════════════════════════════════════
Write-Host "`nGovernance Contract Freshness Check" -ForegroundColor White
Write-Host "   Root: $root" -ForegroundColor DarkGray
Write-Host "   Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray

# ── Build source file lists
$ctrlDir = Join-Path $root 'dotnet-api\Controllers'
$controllerFiles = @()
if (Test-Path $ctrlDir) {
    $controllerFiles = @(Get-ChildItem $ctrlDir -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName.Substring($root.Length + 1) })
}

$compDir = Join-Path $root 'src\components'
$componentFiles = @()
if (Test-Path $compDir) {
    $componentFiles = @(Get-ChildItem $compDir -Filter '*.tsx' -Recurse | ForEach-Object { $_.FullName.Substring($root.Length + 1) })
}

$migDir = Join-Path $root 'dotnet-api\Migrations'
$migrationFile = $null
if (Test-Path $migDir) {
    $latest = Get-ChildItem $migDir -Filter '*.cs' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) { $migrationFile = $latest.FullName.Substring($root.Length + 1) }
}
$dbSources = @('dotnet-api\Models\Models.cs')
if ($migrationFile) { $dbSources = $dbSources + $migrationFile }

# ── Run checks
Write-Header "API Contract"
Test-Contract 'governance/contracts/api-contract.md' $controllerFiles 'api-contract.md vs Controllers/*.cs'

Write-Header "DB Schema"
Test-Contract 'governance/contracts/db-schema.md' $dbSources 'db-schema.md vs Models.cs + migrations'

Write-Header "Component Contracts"
Test-Contract 'governance/contracts/component-contracts.md' $componentFiles 'component-contracts.md vs components/*.tsx'

Write-Header "Architecture Doc"
$archSources = @('dotnet-api\Program.cs', 'dotnet-api\Models\Models.cs', 'src\App.tsx', 'src\apiClient.ts', 'detection-service\main.py')
Test-Contract 'governance/architecture/bit-platform-architecture.md' $archSources 'architecture doc vs key files'

Write-Header "Design Doc"
$designSources = @('dotnet-api\Program.cs', 'dotnet-api\Models\Models.cs', 'dotnet-api\Services\ContentService.cs')
Test-Contract 'governance/design/bit-platform-design.md' $designSources 'design doc vs key files'

Write-Header "Source of Truth Registry"
$sotSrc = @()
if ($controllerFiles.Count -gt 0) { $sotSrc = @($controllerFiles[0]) }
Test-Contract 'governance/architecture/source-of-truth.md' $sotSrc 'source-of-truth.md'

# ═══════════════════════════════════════════════════════════════════════
Write-Host "`n════════════════════════════════════════" -ForegroundColor White
Write-Host "  RESULTS" -ForegroundColor White
Write-Host "════════════════════════════════════════" -ForegroundColor White

if ($script:fresh -gt 0) { Write-Host "  ✓ $($script:fresh) contracts up to date" -ForegroundColor Green }
if ($script:stale -gt 0)  { Write-Host "  ✗ $($script:stale) contracts STALE - update required" -ForegroundColor Red }
if ($script:missing -gt 0) { Write-Host "  ⚠ $($script:missing) contracts MISSING - create them" -ForegroundColor Yellow }

Write-Host ""

if (($script:stale -gt 0) -or ($script:missing -gt 0)) {
    Write-Host "Action required:" -ForegroundColor Yellow
    Write-Host "   1. Review the stale contracts listed above" -ForegroundColor Yellow
    Write-Host "   2. Update them to match the current source files" -ForegroundColor Yellow
    Write-Host "   3. See governance/rules/contract-maintenance.md for guidance" -ForegroundColor Yellow
    Write-Host "   4. Re-run: governance/scripts/validate-contracts.ps1" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}
else {
    Write-Host "All governance contracts are up to date." -ForegroundColor Green
    Write-Host ""
    exit 0
}
