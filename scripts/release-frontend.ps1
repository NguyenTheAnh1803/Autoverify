param(
    [string]$ApiBaseUrl = "/api/v1",
    [string]$Output = "artifacts\frontend"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$webRoot = Join-Path $repoRoot "src\web"
$distPath = Join-Path $webRoot "dist"
$outputPath = Join-Path $repoRoot $Output

$env:VITE_API_BASE_URL = $ApiBaseUrl
npm --prefix $webRoot run build
npm --prefix $webRoot run lint

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputPath | Out-Null
Copy-Item -Path (Join-Path $distPath "*") -Destination $outputPath -Recurse -Force
