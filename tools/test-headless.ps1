# Runs exactly what CI's headless job runs, so green here means green there.
#
# Use this instead of "dotnet test Ash.sln" or "dotnet build Ash.sln". The solution
# includes game/Ash.Game.csproj, which needs the Godot .NET SDK; nothing under src/
# or tests/ does. Going through the solution couples library work to the engine
# toolchain for no benefit, and when it breaks the error points at the wrong thing
# (see tools/check-target-frameworks.ps1).
#
# Run tools/preflight.ps1 first if you want to know what this machine can do.
#
#   -SkipDataChecks   skip the Python steps
#
# Runs under Windows PowerShell and under pwsh on Linux.

param(
    [switch]$SkipDataChecks,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Continue"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$failures = @()

function Invoke-Step($name, [scriptblock]$action) {
    Write-Host ""
    Write-Host "=== $name"
    & $action
    if ($LASTEXITCODE -ne 0) {
        $script:failures += $name
        Write-Host "--- FAILED: $name"
    }
}

# Order matters: the framework check is first because a TFM mismatch makes every
# later failure misleading.
Invoke-Step "Target framework check" { & (Join-Path $PSScriptRoot "check-target-frameworks.ps1") }
Invoke-Step "Architecture check" { & (Join-Path $PSScriptRoot "check-architecture.ps1") }
Invoke-Step "Project settings check" { & (Join-Path $PSScriptRoot "check-project-settings.ps1") }

# Addressed individually rather than via Ash.sln, deliberately. Keep in step with
# the per-project steps in .github/workflows/ci.yml.
$testProjects = @(
    "Ash.Core",
    "Ash.Rules",
    "Ash.Content",
    "Ash.Sim",
    "Ash.Script"
)

foreach ($project in $testProjects) {
    $path = Join-Path $repoRoot "tests/$project.Tests/$project.Tests.csproj"
    if (-not (Test-Path -LiteralPath $path)) {
        $failures += "missing test project $project.Tests"
        Write-Host ""
        Write-Host "--- FAILED: $path does not exist"
        continue
    }

    Invoke-Step "Test $project" { & dotnet test $path --configuration $Configuration --nologo }
}

if (-not $SkipDataChecks) {
    Invoke-Step "Generated-data check" {
        & python (Join-Path $repoRoot "tools/import/gen_attack_tables.py") --check | Out-Null
    }
    Invoke-Step "Attack-curve analysis" {
        & python (Join-Path $repoRoot "tools/import/analyze_attack_table_shape.py") | Out-Null
    }
}

Write-Host ""
Write-Host "========================================"
if ($failures.Count -eq 0) {
    Write-Host "All headless checks passed."
    Write-Host "Note: this does not build game/. That is CI's separate Godot job."
    exit 0
}

Write-Host ("FAILED: " + ($failures -join ", "))
exit 1
