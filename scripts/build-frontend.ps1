param(
    [string]$ApiBaseUrl = "/api/v1"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$webRoot = Join-Path $repoRoot "src\web"

$env:VITE_API_BASE_URL = $ApiBaseUrl
npm --prefix $webRoot run build
npm --prefix $webRoot run lint
