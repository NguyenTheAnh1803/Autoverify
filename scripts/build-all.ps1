param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$IncludeEngine,
    [string]$ApiBaseUrl = "/api/v1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")

& (Join-Path $PSScriptRoot "build-backend.ps1") -Configuration $Configuration
& (Join-Path $PSScriptRoot "build-frontend.ps1") -ApiBaseUrl $ApiBaseUrl

if ($IncludeEngine) {
    & (Join-Path $PSScriptRoot "build-engine.ps1") -Configuration $Configuration
}
