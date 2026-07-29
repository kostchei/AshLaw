# Builds and exports both runnable forms of AshLaw.
#
# Client:
#   artifacts/builds/windows-client/AshLaw.exe
#
# Headless:
#   artifacts/builds/windows-headless/AshLaw.Headless.exe
#
# The matching Godot 4.7 Mono export templates must already be installed.

param(
    [ValidateSet("All", "Client", "Headless")]
    [string]$Target = "All",
    [string]$GodotPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$gameRoot = Join-Path $repoRoot "game"
$gameSolution = Join-Path $gameRoot "Ash.Game.sln"
$buildRoot = Join-Path (Join-Path $repoRoot "artifacts") "builds"
$clientRoot = Join-Path $buildRoot "windows-client"
$headlessRoot = Join-Path $buildRoot "windows-headless"
$clientExe = Join-Path $clientRoot "AshLaw.exe"
$headlessExe = Join-Path $headlessRoot "AshLaw.Headless.exe"
$clientZip = Join-Path $buildRoot "AshLaw-Windows-Test.zip"
$headlessZip = Join-Path $buildRoot "AshLaw-Windows-Headless.zip"

function Resolve-GodotPath {
    param([string]$RequestedPath)

    if ($RequestedPath) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command "godot_console.exe" -ErrorAction SilentlyContinue
    if (-not $command) {
        $command = Get-Command "godot.exe" -ErrorAction SilentlyContinue
    }
    if (-not $command) {
        throw "Godot was not found. Pass -GodotPath with the Godot 4.7 Mono executable."
    }

    $item = Get-Item -LiteralPath $command.Source
    if ($item.LinkType -and $item.Target) {
        return (Resolve-Path -LiteralPath $item.Target[0]).Path
    }

    return $item.FullName
}

function Invoke-Checked {
    param(
        [string]$Description,
        [scriptblock]$Action
    )

    Write-Host $Description
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-GodotExport {
    param(
        [string]$Preset,
        [string]$OutputPath
    )

    $output = @(
        & $godot --headless --path $gameRoot `
            --export-release $Preset $OutputPath 2>&1
    )
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }

    $errors = @($output | Where-Object { $_.ToString() -match '^ERROR:' })
    if ($exitCode -ne 0 -or $errors.Count -gt 0) {
        throw "Godot export '$Preset' failed with exit code $exitCode and $($errors.Count) reported error(s)."
    }
    if (-not (Test-Path -LiteralPath $OutputPath)) {
        throw "Godot export '$Preset' reported success but did not create $OutputPath."
    }
}

$godot = Resolve-GodotPath $GodotPath
$versionOutput = @(& $godot --version 2>&1)
$versionExitCode = $LASTEXITCODE
if ($versionExitCode -ne 0 -or $versionOutput.Count -eq 0) {
    throw "Could not query Godot at $godot (exit $versionExitCode): $versionOutput"
}
$version = $versionOutput[0].ToString().Trim()
if ($version -notmatch '^4\.7') {
    throw "Godot 4.7 Mono is required; found '$version' at $godot."
}

Write-Host "Using $version"
Write-Host "Godot: $godot"

if (-not (Test-Path -LiteralPath $gameSolution)) {
    throw "Godot's C# exporter requires $gameSolution. Create it and add Ash.Game.csproj."
}

Invoke-Checked "Checking target frameworks..." {
    & powershell -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $repoRoot "tools/check-target-frameworks.ps1")
}
Invoke-Checked "Checking Godot project settings..." {
    & powershell -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $repoRoot "tools/check-project-settings.ps1")
}
Invoke-Checked "Restoring the Godot project..." {
    & dotnet restore (Join-Path $gameRoot "Ash.Game.csproj") --ignore-failed-sources
}
Invoke-Checked "Building the Godot project..." {
    & dotnet build (Join-Path $gameRoot "Ash.Game.csproj") --no-restore
}

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

if ($Target -in @("All", "Client")) {
    if (Test-Path -LiteralPath $clientRoot) {
        Remove-Item -LiteralPath $clientRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $clientRoot -Force | Out-Null
    Write-Host "Exporting Windows client..."
    Invoke-GodotExport "Windows Client" $clientExe
    Write-Host "Packing Windows client..."
    Compress-Archive `
        -Path (Join-Path $clientRoot "*") `
        -DestinationPath $clientZip `
        -CompressionLevel Optimal `
        -Force
}

if ($Target -in @("All", "Headless")) {
    if (Test-Path -LiteralPath $headlessRoot) {
        Remove-Item -LiteralPath $headlessRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $headlessRoot -Force | Out-Null
    Write-Host "Exporting Windows headless build..."
    Invoke-GodotExport "Windows Headless" $headlessExe

    $smokeLauncher = @"
@echo off
echo Running AshLaw headless smoke test...
"%~dp0AshLaw.Headless.exe" --headless --quit-after 2
if errorlevel 1 (
  echo FAILED with exit code %errorlevel%.
  pause
  exit /b %errorlevel%
)
echo PASSED.
pause
"@
    Set-Content `
        -LiteralPath (Join-Path $headlessRoot "Run Headless Smoke Test.cmd") `
        -Value $smokeLauncher `
        -Encoding Ascii
    Write-Host "Packing Windows headless build..."
    Compress-Archive `
        -Path (Join-Path $headlessRoot "*") `
        -DestinationPath $headlessZip `
        -CompressionLevel Optimal `
        -Force
}

Write-Host ""
Write-Host "Export complete."
if ($Target -in @("All", "Client")) {
    Write-Host "  Play:     $clientExe"
    Write-Host "  Package:  $clientZip"
}
if ($Target -in @("All", "Headless")) {
    Write-Host "  Headless: $headlessExe"
    Write-Host "  Package:  $headlessZip"
}
