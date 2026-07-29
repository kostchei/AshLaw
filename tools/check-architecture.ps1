# Asserts that the headless source projects never take a Godot dependency, so the
# rules/sim layers stay testable without an engine. Runs under Windows PowerShell
# and under pwsh on Linux CI, so paths are built with Join-Path rather than
# embedded separators.

$ErrorActionPreference = "Stop"

$sourceRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot "..") "src")

# Exclude build output: obj/ holds generated files that can legitimately mention
# Godot when a project reference chain passes through the game assembly.
$violations = Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Select-String -Pattern '^\s*using\s+Godot(?:\.|;)' |
    ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }

if ($violations) {
    Write-Host "Headless source projects must not reference Godot:"
    $violations | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "Architecture check passed: src contains no Godot using directives."
exit 0
