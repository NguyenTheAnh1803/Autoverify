param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Output = "artifacts\backend"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\backend\AutoVerify.Api\AutoVerify.Api.csproj"
$outputPath = Join-Path $repoRoot $Output

dotnet publish $project -c $Configuration -o $outputPath
