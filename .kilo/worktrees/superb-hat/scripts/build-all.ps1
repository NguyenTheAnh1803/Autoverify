param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")

dotnet build (Join-Path $repoRoot "AutoVerify.sln") -c $Configuration

npm --prefix (Join-Path $repoRoot "src\web") run build
npm --prefix (Join-Path $repoRoot "src\web") run lint

& (Join-Path $PSScriptRoot "build-engine.ps1") -Configuration $Configuration
