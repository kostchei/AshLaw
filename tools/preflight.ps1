# Reports what this machine can actually build, and why not, in one command.
#
# Exists because a session that hits a build failure here has historically had to
# guess at the cause -- and guessed wrong. The Godot target-framework mismatch
# surfaces as NU1201, a NuGet error raised during restore, which reads like an
# unresolvable SDK rather than what it is.
#
# Exit code reflects HEADLESS capability only. Godot problems are reported as
# warnings, never failures, because nothing under src/ or tests/ needs Godot and a
# missing engine must not block library work.
#
# Runs under Windows PowerShell and under pwsh on Linux.

$ErrorActionPreference = "Continue"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$headlessProblems = @()
$godotWarnings = @()

function Write-Line($status, $name, $detail) {
    Write-Host ("  {0,-8} {1,-22} {2}" -f $status, $name, $detail)
}

Write-Host ""
Write-Host "U8_Ash preflight"
Write-Host "----------------"

# --- .NET SDK -------------------------------------------------------------
$globalJsonPath = Join-Path $repoRoot "global.json"
$pinnedSdk = $null
if (Test-Path -LiteralPath $globalJsonPath) {
    $pinnedSdk = (Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json).sdk.version
}

$dotnetVersion = $null
try { $dotnetVersion = (& dotnet --version).Trim() } catch { }

if ($dotnetVersion) {
    if ($pinnedSdk -and $dotnetVersion -ne $pinnedSdk) {
        Write-Line "OK" ".NET SDK" "$dotnetVersion (global.json pins $pinnedSdk; rollForward applies)"
    }
    else {
        Write-Line "OK" ".NET SDK" $dotnetVersion
    }
}
else {
    Write-Line "MISSING" ".NET SDK" "dotnet not on PATH. Install the SDK pinned in global.json."
    $headlessProblems += "no .NET SDK"
}

# --- Target framework consistency -----------------------------------------
# The failure this whole script exists for. Run it before anything that builds.
$tfmScript = Join-Path $PSScriptRoot "check-target-frameworks.ps1"
$tfmOutput = & $tfmScript
if ($LASTEXITCODE -eq 0) {
    Write-Line "OK" "Target frameworks" ($tfmOutput | Select-Object -Last 1)
}
else {
    Write-Line "BROKEN" "Target frameworks" "mismatch -- see below"
    $tfmOutput | ForEach-Object { Write-Host "           $_" }
    $headlessProblems += "target framework mismatch"
}

# --- Python ---------------------------------------------------------------
$pythonVersion = $null
try { $pythonVersion = (& python --version).Trim() } catch { }
if ($pythonVersion) {
    Write-Line "OK" "Python" $pythonVersion
}
else {
    Write-Line "MISSING" "Python" "needed only by tools/import/*.py (data checks)."
    $headlessProblems += "no Python"
}

# --- Godot .NET SDK package ----------------------------------------------
$gameProject = Join-Path (Join-Path $repoRoot "game") "Ash.Game.csproj"
$sdkVersion = $null
if (Test-Path -LiteralPath $gameProject) {
    $sdkMatch = [regex]::Match(
        (Get-Content -LiteralPath $gameProject -Raw),
        'Sdk="Godot\.NET\.Sdk/([^"]+)"')
    if ($sdkMatch.Success) { $sdkVersion = $sdkMatch.Groups[1].Value }
}

if ($sdkVersion) {
    $home_ = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
    $sdkPath = Join-Path (Join-Path (Join-Path $home_ ".nuget") "packages") "godot.net.sdk/$sdkVersion"
    if (Test-Path -LiteralPath $sdkPath) {
        Write-Line "OK" "Godot.NET.Sdk" "$sdkVersion cached locally"
    }
    else {
        Write-Line "WARN" "Godot.NET.Sdk" "$sdkVersion not in the NuGet cache; needs network on first restore."
        $godotWarnings += "Godot.NET.Sdk $sdkVersion uncached"
    }
}
else {
    Write-Line "WARN" "Godot.NET.Sdk" "could not read the SDK version from game/Ash.Game.csproj."
    $godotWarnings += "unreadable Godot SDK version"
}

# --- Godot editor ---------------------------------------------------------
$godotExe = Get-Command godot -ErrorAction SilentlyContinue
if ($godotExe) {
    $godotVersion = "unknown"
    try { $godotVersion = (& godot --version | Select-Object -Last 1).Trim() } catch { }
    Write-Line "OK" "Godot editor" $godotVersion
}
else {
    Write-Line "WARN" "Godot editor" "not on PATH. Only needed to RUN the game, not to build or test."
    $godotWarnings += "no Godot executable"
}

# --- Verdict --------------------------------------------------------------
Write-Host ""
if ($headlessProblems.Count -eq 0) {
    Write-Host "Headless work is fully available. Run: pwsh ./tools/test-headless.ps1"
}
else {
    Write-Host ("Headless work is BLOCKED: " + ($headlessProblems -join ", "))
}

if ($godotWarnings.Count -gt 0) {
    Write-Host ("Godot is degraded: " + ($godotWarnings -join ", "))
    Write-Host "That blocks only 'dotnet build Ash.sln' and game/. It does not block src/ or tests/."
    Write-Host "Never 'fix' it by downgrading the shared libraries to net8.0."
}
else {
    Write-Host "Godot builds are available too."
}
Write-Host ""

if ($headlessProblems.Count -gt 0) { exit 1 }
exit 0
