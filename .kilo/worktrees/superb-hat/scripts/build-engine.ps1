param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$engineRoot = Join-Path $repoRoot "src\engine"
$buildRoot = Join-Path $repoRoot "build\engine"
$directBuildRoot = Join-Path $repoRoot "build\engine-direct"

function Test-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

if (Test-CommandAvailable "cmake") {
    if (-not (Test-Path -LiteralPath $buildRoot)) {
        New-Item -ItemType Directory -Path $buildRoot | Out-Null
    }

    cmake -S $engineRoot -B $buildRoot -DCMAKE_BUILD_TYPE=$Configuration
    cmake --build $buildRoot --config $Configuration
    ctest --test-dir $buildRoot -C $Configuration --output-on-failure
    exit 0
}

if (-not (Test-Path -LiteralPath $directBuildRoot)) {
    New-Item -ItemType Directory -Path $directBuildRoot | Out-Null
}

$includePath = Join-Path $engineRoot "include"
$coreSource = Join-Path $engineRoot "src\anomaly_engine.cpp"
$mainSource = Join-Path $engineRoot "src\main.cpp"
$testSource = Join-Path $engineRoot "tests\anomaly_engine_tests.cpp"

if (Test-CommandAvailable "cl") {
    $engineExe = Join-Path $directBuildRoot "autoverify-engine.exe"
    $testExe = Join-Path $directBuildRoot "autoverify-engine-tests.exe"

    cl /nologo /std:c++20 /EHsc /I $includePath /Fe:$engineExe $coreSource $mainSource
    cl /nologo /std:c++20 /EHsc /I $includePath /Fe:$testExe $coreSource $testSource
    & $testExe
    exit 0
}

if (Test-CommandAvailable "g++") {
    $engineExe = Join-Path $directBuildRoot "autoverify-engine.exe"
    $testExe = Join-Path $directBuildRoot "autoverify-engine-tests.exe"

    g++ -std=c++20 -Wall -Wextra -pedantic -I $includePath $coreSource $mainSource -o $engineExe
    g++ -std=c++20 -Wall -Wextra -pedantic -I $includePath $coreSource $testSource -o $testExe
    & $testExe
    exit 0
}

if (Test-CommandAvailable "clang++") {
    $engineExe = Join-Path $directBuildRoot "autoverify-engine.exe"
    $testExe = Join-Path $directBuildRoot "autoverify-engine-tests.exe"

    clang++ -std=c++20 -Wall -Wextra -pedantic -I $includePath $coreSource $mainSource -o $engineExe
    clang++ -std=c++20 -Wall -Wextra -pedantic -I $includePath $coreSource $testSource -o $testExe
    & $testExe
    exit 0
}

throw "No supported C++ build tool was found. Install CMake with Visual Studio Build Tools, MSVC cl.exe, MinGW g++, or LLVM clang++, then rerun scripts/build-engine.ps1."
