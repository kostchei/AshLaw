# Asserts that game/project.godot still matches build plan section 2.8.
#
# Ash.Core.ViewportScaling models these settings. If they drift, the headless
# viewport round-trip tests keep passing while the running game maps input to the
# wrong pixel -- a mismatch that is hard to spot by eye and easy to introduce,
# since the Godot editor rewrites project.godot every time the project is opened.
#
# This parses the file rather than asking the engine, for two reasons: it runs in
# the headless CI job with no Godot installed, and a setting that is *absent*
# silently falls back to a Godot default that is wrong for this project
# ("disabled" stretch, "ignore" aspect, "fractional" scale). Requiring the key to
# be present and correct catches that; querying the engine would not.
#
# Runs under Windows PowerShell and under pwsh on Linux.

$ErrorActionPreference = "Stop"

$projectFile = Join-Path (Join-Path (Join-Path $PSScriptRoot "..") "game") "project.godot"
if (-not (Test-Path -LiteralPath $projectFile)) {
    Write-Host "Cannot find $projectFile."
    exit 1
}

# Fully-qualified setting name -> required literal value, exactly as written in the
# file (quotes included for string settings).
$expected = [ordered]@{
    # "viewport" scales the whole 1280x800 framebuffer. "canvas_items" would scale
    # drawing commands instead, giving sub-pixel sprite positions.
    "display/window/stretch/mode"                            = '"viewport"'
    "display/window/stretch/aspect"                          = '"keep"'
    # Forbids fractional scales, so slack goes to the letterbox, not the pixel grid.
    "display/window/stretch/scale_mode"                      = '"integer"'
    "display/window/size/viewport_width"                     = "1280"
    "display/window/size/viewport_height"                    = "800"
    # 0 is Nearest, with no mipmaps.
    "rendering/textures/canvas_textures/default_texture_filter" = "0"
}

$actual = @{}
$section = ""
foreach ($rawLine in Get-Content -LiteralPath $projectFile) {
    $line = $rawLine.Trim()
    if ($line.Length -eq 0 -or $line.StartsWith(";")) { continue }

    if ($line.StartsWith("[") -and $line.EndsWith("]")) {
        $section = $line.Substring(1, $line.Length - 2)
        continue
    }

    $separator = $line.IndexOf("=")
    if ($separator -lt 1) { continue }

    $key = $line.Substring(0, $separator).Trim()
    $value = $line.Substring($separator + 1).Trim()
    $qualified = if ($section) { "$section/$key" } else { $key }
    $actual[$qualified] = $value
}

$failures = @()
foreach ($key in $expected.Keys) {
    if (-not $actual.ContainsKey($key)) {
        $failures += "  $key is absent; Godot would fall back to a default that is wrong here. Expected $($expected[$key])."
    }
    elseif ($actual[$key] -cne $expected[$key]) {
        $failures += "  $key = $($actual[$key]); expected $($expected[$key])."
    }
}

if ($failures.Count -gt 0) {
    Write-Host "game/project.godot does not match build plan section 2.8:"
    $failures | ForEach-Object { Write-Host $_ }
    Write-Host "Ash.Core.ViewportScaling models these settings. Fix project.godot, or"
    Write-Host "change ViewportScaling and this check together."
    exit 1
}

Write-Host "Project settings check passed: game/project.godot matches build plan 2.8."
exit 0
