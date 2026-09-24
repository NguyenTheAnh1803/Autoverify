param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Output = "artifacts\engine"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$outputPath = Join-Path $repoRoot $Output

& (Join-Path $PSScriptRoot "build-engine.ps1") -Configuration $Configuration

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputPath | Out-Null

$buildRoot = Join-Path $repoRoot "build\engine"
$directBuildRoot = Join-Path $repoRoot "build\engine-direct"

if (Test-Path $buildRoot) {
    $builtExe = Get-ChildItem -Path $buildRoot -Filter "autoverify-engine.exe" -Recurse | Select-Object -First 1
    if ($builtExe) {
        Copy-Item -LiteralPath $builtExe.FullName -Destination $outputPath -Force
    }
    $builtLib = Get-ChildItem -Path $buildRoot -Filter "*autoverify_engine_core*" -Recurse | Select-Object -First 1
    if ($builtLib) {
        Copy-Item -LiteralPath $builtLib.FullName -Destination $outputPath -Force
    }
}
elseif (Test-Path $directBuildRoot) {
    Copy-Item -Path (Join-Path $directBuildRoot "*") -Destination $outputPath -Recurse -Force
}
