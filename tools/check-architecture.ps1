$ErrorActionPreference = "Stop"

$sourceRoot = Resolve-Path (Join-Path $PSScriptRoot "..\src")
$violations = Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.cs" |
    Select-String -Pattern '^\s*using\s+Godot(?:\.|;)' |
    ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }

if ($violations) {
    Write-Error ("Headless source projects must not reference Godot:`n" + ($violations -join "`n"))
}

Write-Output "Architecture check passed: src contains no Godot using directives."

