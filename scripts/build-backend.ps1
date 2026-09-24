param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\backend\AutoVerify.Api\AutoVerify.Api.csproj"

dotnet build $project -c $Configuration
