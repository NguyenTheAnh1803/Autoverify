param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ApiBaseUrl = "/api/v1",
    [switch]$IncludeEngine = $true
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$artifactsRoot = Join-Path $repoRoot "artifacts"
$localPublish = Join-Path $artifactsRoot "autoverify-local"
$backendPublish = Join-Path $artifactsRoot "backend"
$frontendPublish = Join-Path $artifactsRoot "frontend"
$enginePublish = Join-Path $artifactsRoot "engine"
$project = Join-Path $repoRoot "src\backend\AutoVerify.Api\AutoVerify.Api.csproj"
$webRoot = Join-Path $repoRoot "src\web"
$distPath = Join-Path $webRoot "dist"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Building & Releasing AutoVerify System " -ForegroundColor Cyan
Write-Host " Target: $artifactsRoot                 " -ForegroundColor Cyan
Write-Host " Config: $Configuration                 " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Build & Lint Frontend
Write-Host "`n[1/4] Building Frontend UI..." -ForegroundColor Yellow
$env:VITE_API_BASE_URL = $ApiBaseUrl
npm --prefix $webRoot run build
npm --prefix $webRoot run lint

# 2. Embed into Backend wwwroot
Write-Host "`n[2/4] Embedding Frontend into Backend wwwroot..." -ForegroundColor Yellow
$wwwroot = Join-Path $repoRoot "src\backend\AutoVerify.Api\wwwroot"
if (Test-Path -LiteralPath $wwwroot) {
    Remove-Item -LiteralPath $wwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $distPath "*") -Destination $wwwroot -Recurse -Force

# 3. Publish Backend & Standalone App into artifacts/
Write-Host "`n[3/4] Publishing Backend to artifacts/autoverify-local & artifacts/backend..." -ForegroundColor Yellow
dotnet publish $project -c $Configuration -o $localPublish
dotnet publish $project -c $Configuration -o $backendPublish

# Copy frontend standalone package
if (Test-Path -LiteralPath $frontendPublish) {
    Remove-Item -LiteralPath $frontendPublish -Recurse -Force
}
New-Item -ItemType Directory -Path $frontendPublish | Out-Null
Copy-Item -Path (Join-Path $distPath "*") -Destination $frontendPublish -Recurse -Force

# 4. Build C++ Engine
if ($IncludeEngine) {
    Write-Host "`n[4/4] Building C++ Anomaly Engine..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "release-engine.ps1") -Configuration $Configuration -Output "artifacts\engine"
    
    # Also copy engine binaries into autoverify-local
    $builtExe = Join-Path $enginePublish "autoverify-engine.exe"
    if (Test-Path -LiteralPath $builtExe) {
        Copy-Item -LiteralPath $builtExe -Destination $localPublish -Force
    }
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " All artifacts successfully built to:  " -ForegroundColor Green
Write-Host " $artifactsRoot                        " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
