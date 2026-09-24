param(
    [string]$ApiBaseUrl = "/api/v1"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$webRoot = Join-Path $repoRoot "src\web"
$distRoot = Join-Path $webRoot "dist"
$wwwroot = Join-Path $repoRoot "src\backend\AutoVerify.Api\wwwroot"

$env:VITE_API_BASE_URL = $ApiBaseUrl
npm --prefix $webRoot run build
npm --prefix $webRoot run lint

if (Test-Path -LiteralPath $wwwroot) {
    Remove-Item -LiteralPath $wwwroot -Recurse -Force
}

New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $distRoot "*") -Destination $wwwroot -Recurse -Force
