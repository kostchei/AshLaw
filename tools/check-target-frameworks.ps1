# Asserts that every project targets the same framework as Directory.Build.props.
#
# This exists because the Godot editor rewrites game/Ash.Game.csproj whenever it
# regenerates the project, and writes its own default TargetFramework -- net8.0 for
# Godot 4.x -- while every shared library here targets net10.0.
#
# The resulting failure is badly misleading. A net8.0 project cannot reference
# net10.0 assemblies, but the build error names the ProjectReferences, not the
# framework, so it reads like a broken dependency or an SDK that will not resolve.
# Build plan section 0.1 calls mismatched TFMs "a common source of misleading
# runtime load errors" and says to pin every project to the same TFM. This is the
# check that makes that instruction enforceable rather than advisory.
#
# Deliberately a text check, not an MSBuild evaluation: it must run in the headless
# CI job, where the Godot SDK is not available and evaluating game/Ash.Game.csproj
# would itself fail.
#
# Runs under Windows PowerShell and under pwsh on Linux.

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$propsFile = Join-Path $repoRoot "Directory.Build.props"

if (-not (Test-Path -LiteralPath $propsFile)) {
    Write-Output "Cannot find $propsFile."
    exit 1
}

$propsText = Get-Content -LiteralPath $propsFile -Raw
$propsMatch = [regex]::Match($propsText, '<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>')
if (-not $propsMatch.Success) {
    Write-Output "Directory.Build.props declares no <TargetFramework>; nothing to check against."
    exit 1
}

$expected = $propsMatch.Groups[1].Value
$failures = @()

$projects = Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter "*.csproj" |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts|\.godot)[\\/]' }

foreach ($project in $projects) {
    $text = Get-Content -LiteralPath $project.FullName -Raw
    $relative = $project.FullName.Substring($repoRoot.Path.Length).TrimStart('\', '/')

    # Plural form: a project here should never multi-target.
    $plural = [regex]::Match($text, '<TargetFrameworks>\s*([^<]+)\s*</TargetFrameworks>')
    if ($plural.Success) {
        $failures += "  ${relative}: declares <TargetFrameworks>$($plural.Groups[1].Value)</TargetFrameworks>; this repo single-targets $expected."
        continue
    }

    $single = [regex]::Match($text, '<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>')
    if (-not $single.Success) {
        # Inherits from Directory.Build.props, which is correct by construction.
        continue
    }

    $actual = $single.Groups[1].Value
    if ($actual -cne $expected) {
        $failures += "  ${relative}: targets $actual, but Directory.Build.props sets $expected."
    }
}

if ($failures.Count -gt 0) {
    Write-Output "Target framework mismatch:"
    $failures | ForEach-Object { Write-Output $_ }
    Write-Output ""
    Write-Output "If this is game/Ash.Game.csproj, the Godot editor almost certainly rewrote it."
    Write-Output "Set it back to $expected. Do not 'fix' this by downgrading the libraries:"
    Write-Output "the shared projects target $expected deliberately (see global.json)."
    exit 1
}

Write-Output "Target framework check passed: all $($projects.Count) projects target $expected."
exit 0
