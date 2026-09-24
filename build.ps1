param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SkipFrontend,
    [switch]$SkipPublish,
    [string]$ApiBaseUrl = "/api/v1"
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$srcBackend = Join-Path $repoRoot "src\backend\AutoVerify.Api"
$backendCsproj = Join-Path $srcBackend "AutoVerify.Api.csproj"
$testsCsproj = Join-Path $repoRoot "tests\AutoVerify.Analytics.Tests\AutoVerify.Analytics.Tests.csproj"
$webRoot = Join-Path $repoRoot "src\web"
$webDist = Join-Path $webRoot "dist"
$backendWwwroot = Join-Path $srcBackend "wwwroot"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$appPublishDir = Join-Path $artifactsRoot "autoverify-app"

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Log-Step([string]$msg, [string]$step) {
    Write-Host ""
    Write-Host "[$step] $msg" -ForegroundColor Cyan
}

function Log-Ok([string]$msg) {
    Write-Host "[OK] $msg" -ForegroundColor Green
}

function Log-Warn([string]$msg) {
    Write-Host "[WARN] $msg" -ForegroundColor Yellow
}

$shouldPublish = -not $SkipPublish

Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host "              AutoVerify System - Full Build              " -ForegroundColor Magenta
Write-Host " Configuration : $Configuration                           " -ForegroundColor Magenta
Write-Host " Target Output : artifacts/autoverify-app                 " -ForegroundColor Magenta
Write-Host " Tests         : $(if ($SkipTests) { 'Skipped' } else { 'Run' }) " -ForegroundColor Magenta
Write-Host " Frontend      : $(if ($SkipFrontend) { 'Skipped' } else { 'Build & Embed' }) " -ForegroundColor Magenta
Write-Host " Publish       : $(if ($shouldPublish) { 'Always (artifacts/autoverify-app)' } else { 'Skipped (-SkipPublish)' }) " -ForegroundColor Magenta
Write-Host "==========================================================" -ForegroundColor Magenta

# STEP 1: Build Frontend UI
if (-not $SkipFrontend) {
    Log-Step "Building Frontend UI (React 19 + TypeScript + Vite)..." "1/4"
    if (-not (Test-Path -LiteralPath (Join-Path $webRoot "node_modules"))) {
        Write-Host "Installing frontend dependencies (npm install)..." -ForegroundColor Gray
        npm --prefix $webRoot install
    }

    $env:VITE_API_BASE_URL = $ApiBaseUrl
    npm --prefix $webRoot run build

    Log-Ok "Frontend built successfully to: $webDist"

    # Embed frontend into backend wwwroot
    Write-Host "Embedding static assets into backend wwwroot..." -ForegroundColor Gray
    if (Test-Path -LiteralPath $backendWwwroot) {
        Remove-Item -LiteralPath $backendWwwroot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $backendWwwroot | Out-Null
    Copy-Item -Path (Join-Path $webDist "*") -Destination $backendWwwroot -Recurse -Force
    Log-Ok "Frontend embedded into: $backendWwwroot"
} else {
    Log-Warn "Frontend build step skipped (-SkipFrontend)."
}

# STEP 2: Build Backend (.NET 8)
Log-Step "Building Backend API & Test Suite (.NET 8)..." "2/4"
dotnet build $backendCsproj -c $Configuration --nologo
dotnet build $testsCsproj -c $Configuration --nologo
Log-Ok "Backend & Test builds completed successfully ($Configuration)."

# STEP 3: Run Unit Tests
if (-not $SkipTests) {
    Log-Step "Running Unit Tests (AutoVerify.Analytics.Tests)..." "3/4"
    dotnet test $testsCsproj -c $Configuration --no-build --nologo --logger "console;verbosity=normal"
    Log-Ok "All unit tests passed."
} else {
    Log-Warn "Unit testing step skipped (-SkipTests)."
}

# STEP 4: Publish to artifacts/autoverify-app (Always by default)
if ($shouldPublish) {
    Log-Step "Publishing complete package to artifacts/autoverify-app..." "4/4"
    
    # Gracefully stop any running AutoVerify.Api process before cleaning
    Get-Process -Name "AutoVerify.Api" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300

    $tempLogsDir = Join-Path $env:TEMP "autoverify_logs_backup"
    $appLogsDir = Join-Path $appPublishDir "logs"
    if (Test-Path -LiteralPath $appLogsDir) {
        if (Test-Path -LiteralPath $tempLogsDir) { Remove-Item -LiteralPath $tempLogsDir -Recurse -Force -ErrorAction SilentlyContinue }
        Copy-Item -Path $appLogsDir -Destination $tempLogsDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $appPublishDir) {
        Remove-Item -LiteralPath $appPublishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $appPublishDir | Out-Null

    dotnet publish $backendCsproj -c $Configuration -o $appPublishDir --nologo

    if (Test-Path -LiteralPath $tempLogsDir) {
        if (-not (Test-Path -LiteralPath $appLogsDir)) { New-Item -ItemType Directory -Path $appLogsDir | Out-Null }
        Copy-Item -Path (Join-Path $tempLogsDir "*") -Destination $appLogsDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $tempLogsDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Double check wwwroot is embedded in publish directory
    $publishWwwroot = Join-Path $appPublishDir "wwwroot"
    if (-not (Test-Path -LiteralPath $publishWwwroot) -and (Test-Path -LiteralPath $webDist)) {
        Copy-Item -Path (Join-Path $webDist "*") -Destination $publishWwwroot -Recurse -Force
    }

    Log-Ok "Ready-to-run package published to: $appPublishDir"
} else {
    Log-Warn "Publish step skipped (-SkipPublish)."
}

$stopwatch.Stop()
$elapsed = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)

$runExePath = Join-Path $appPublishDir "AutoVerify.Api.exe"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "             BUILD COMPLETED SUCCESSFULLY!                " -ForegroundColor Green
Write-Host " Total time elapsed : $elapsed seconds                    " -ForegroundColor Green
if ($shouldPublish) {
    Write-Host " Artifacts folder   : $appPublishDir                      " -ForegroundColor Green
    Write-Host " Run executable     : $runExePath                         " -ForegroundColor Green
    Write-Host " Web UI embedded    : $appPublishDir\wwwroot              " -ForegroundColor Green
} else {
    Write-Host " Output binary      : src\backend\AutoVerify.Api\bin\$Configuration\net8.0" -ForegroundColor Yellow
}
Write-Host "==========================================================" -ForegroundColor Green
