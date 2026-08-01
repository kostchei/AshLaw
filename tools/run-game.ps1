# Runs the Godot game and reports whether it actually worked.
#
# Why this script exists
# ----------------------
# Two traps make "just run Godot" unreliable here, and both look like a hang:
#
# 1. WinGet puts "godot" on PATH as a symlink under WinGet/Links. Godot mono
#    resolves GodotSharp/Api relative to its own executable, so launching
#    through that name makes it search WinGet/Links/GodotSharp, find nothing,
#    and block on a modal ".NET assemblies not found" dialog. No C# ever runs,
#    and headless there is nothing on stdout to say so. This script always
#    launches the real file the link points at, and refuses to start if
#    GodotSharp is not beside it.
#
# 2. The engine's exit code is not a verdict on the game. A scene can fail to
#    load, a script can throw every frame, and the process still exits zero.
#
# So the game reports for itself. With "--smoke-test" it plays a scripted input
# sequence, validates the object/physics/index invariants, writes a report file
# and asks Godot to quit. Success is "the report exists, says invariants=ok, and
# the log holds no error that is not known shutdown noise".
#
#   pwsh ./tools/run-game.ps1                    headless smoke run
#   pwsh ./tools/run-game.ps1 -Screenshot        windowed run, saves a PNG
#   pwsh ./tools/run-game.ps1 -Demo -Goto 28,6 -Screenshot
#                                                the acceptance map, walked to a
#                                                cell, then shot
#   pwsh ./tools/run-game.ps1 -WorldSeed 4242    a different generated world
#   pwsh ./tools/run-game.ps1 -Interactive       just play it, no reporting
#
# Artifacts land in artifacts/run-game/ (git-ignored).

param(
    # Frames to run before the report is taken. The scripted walk takes the six
    # steps a six-second round allows, over the first 60 frames.
    [int]$Frames = 90,

    # Run with a window instead of headless. Implied by -Screenshot, because a
    # headless run has no renderer and so cannot produce an image.
    [switch]$Windowed,

    # Capture the viewport to artifacts/run-game/screenshot.png.
    [switch]$Screenshot,

    # Walk the Avatar to this grid cell ("x,y") before reporting, so a run can
    # be pointed at the feature under review.
    [string]$Goto,

    # Turn on the F3 overlay (collision volumes, elevation, motion, support).
    [switch]$DebugOverlay,

    # Open the carried-gear panel, so a screenshot shows it.
    [switch]$Backpack,

    # Open the help overlay, so a screenshot shows it.
    [switch]$Help,

    # Keep the initial Human character-creation screen open during a smoke
    # screenshot instead of auto-confirming its deterministic draft.
    [switch]$CharacterCreation,

    # Exercise the in-game save and load commands during the run.
    [switch]$SaveLoad,

    # Stage and resolve one melee action, then report event-driven presentation.
    [switch]$Combat,

    # Pick an object up and drop it into the backpack during the run.
    [switch]$DragDrop,

    # With -DragDrop, keep the object in hand instead of dropping it, so a
    # screenshot shows a live drag.
    [switch]$Hold,

    # Play the hand-built acceptance map instead of a generated world. It is
    # the only map with the physics area — the trestle, the stacked crates and
    # the bridge over the pit — so -DragDrop and -Goto runs aimed at those want
    # this.
    [switch]$Demo,

    # Generate the world from this seed instead of the built-in one.
    [string]$WorldSeed,

    # Hand the game to a human: no smoke run, no timeout, no reaping.
    [switch]$Interactive,

    # How long to wait for the report before giving up.
    [int]$TimeoutSeconds = 180,

    [string]$Configuration = "Debug",

    # Skip "dotnet build". Godot builds the assembly itself, but building first
    # turns a C# error into a C# error message instead of a silent no-report.
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$gameDir = Join-Path $repoRoot "game"
$outputDir = Join-Path $repoRoot "artifacts/run-game"
$reportPath = Join-Path $outputDir "smoke-report.txt"
$logPath = Join-Path $outputDir "godot.log"
$screenshotPath = Join-Path $outputDir "screenshot.png"

# Shutdown noise from Godot's own teardown, not from this project. These lines
# are printed after the game has finished running and must not fail the check.
$knownExitNoise = @(
    "RID allocations of type",
    "Pages in use exist at exit",
    "A Thread object is being destroyed",
    "at: ~PagedAllocator",
    "at: ~Thread"
)

function Get-GodotCommand {
    $source = $null
    foreach ($candidate in @($env:GODOT, "godot", "godot4", "Godot_v4.7-stable_mono_win64")) {
        if (-not $candidate) { continue }
        $resolved = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($resolved) { $source = $resolved.Source; break }
    }

    if (-not $source) {
        throw "Godot was not found. Install Godot 4.7 mono, or set `$env:GODOT to its executable."
    }

    # Godot mono looks for GodotSharp/Api next to its own executable. A WinGet
    # install puts "godot" on PATH as a symlink under WinGet/Links, so launching
    # through that name makes the engine search WinGet/Links/GodotSharp, find
    # nothing, and block on a modal ".NET assemblies not found" dialog — which
    # looks exactly like a hang, because the process never exits and, headless,
    # never says why. Always launch the real file the link points at.
    # PowerShell 7 exposes LinkTarget; Windows PowerShell 5.1 exposes Target as
    # an array. Both appear in this repo's tooling, so handle both.
    $item = Get-Item $source
    for ($hop = 0; $hop -lt 8; $hop++) {
        $target = $item.LinkTarget
        if (-not $target) { $target = $item.Target | Select-Object -First 1 }
        if (-not $target) { break }
        $item = Get-Item $target
    }

    $godotDir = Split-Path $item.FullName -Parent

    # The console build writes to stdout; the plain one detaches from it on
    # Windows, so the log would be empty.
    $console = Join-Path $godotDir "$($item.BaseName)_console.exe"
    $executable = if (Test-Path $console) { $console } else { $item.FullName }

    if (-not (Test-Path (Join-Path $godotDir "GodotSharp/Api"))) {
        throw "The Godot at '$executable' has no GodotSharp/Api directory next to it. " +
            "That is a non-mono build, or a launcher shim: point `$env:GODOT at the real " +
            "Godot 4.7 *mono* executable."
    }

    return $executable
}

$godot = Get-GodotCommand
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if (-not $SkipBuild) {
    Write-Host "=== Building game/Ash.Game.csproj ($Configuration)"
    dotnet build (Join-Path $gameDir "Ash.Game.csproj") -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "--- FAILED: the game project does not build."
        exit 1
    }
}

if ($Interactive) {
    Write-Host "=== Launching Godot (interactive). Close the window when done."
    $playArgs = @("--debug-overlay")
    if ($Demo) { $playArgs += "--demo" }
    if ($WorldSeed) { $playArgs += "--world-seed=$WorldSeed" }
    & $godot --path $gameDir -- @playArgs
    exit 0
}

if ($Screenshot) { $Windowed = $true }

foreach ($stale in @($reportPath, $logPath, $screenshotPath)) {
    if (Test-Path $stale) { Remove-Item $stale -Force }
}

$godotArgs = @()
if (-not $Windowed) { $godotArgs += "--headless" }
$godotArgs += @(
    "--path", $gameDir,
    # A safety net only: the game quits itself once the report is written. If
    # the smoke logic never runs, this stops the frame loop anyway.
    "--quit-after", ($Frames * 4),
    "--",
    "--smoke-test",
    "--smoke-frames=$Frames",
    "--smoke-report=$reportPath"
)
if ($Screenshot) { $godotArgs += "--screenshot=$screenshotPath" }
if ($Goto) { $godotArgs += "--smoke-goto=$Goto" }
if ($DebugOverlay) { $godotArgs += "--debug-overlay" }
if ($Backpack) { $godotArgs += "--backpack-open" }
if ($Help) { $godotArgs += "--help-open" }
if ($CharacterCreation) { $godotArgs += "--character-creation-open" }
if ($SaveLoad) { $godotArgs += "--smoke-save-load" }
if ($Combat) { $godotArgs += "--smoke-combat" }
if ($DragDrop) { $godotArgs += "--smoke-drag-drop" }
if ($Hold) { $godotArgs += "--smoke-hold" }
if ($Demo) { $godotArgs += "--demo" }
if ($WorldSeed) { $godotArgs += "--world-seed=$WorldSeed" }

Write-Host "=== Running $(if ($Windowed) { 'windowed' } else { 'headless' }) smoke run ($Frames frames)"
$process = Start-Process -FilePath $godot -ArgumentList $godotArgs -PassThru -NoNewWindow `
    -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Test-Path $reportPath)) {
    if ($process.HasExited) {
        # Godot exited without reporting: a crash, a scene that never loaded, or
        # an exception before the report was written. The log says which.
        Start-Sleep -Milliseconds 200
        break
    }

    if ((Get-Date) -gt $deadline) { break }
    Start-Sleep -Milliseconds 250
}

# Godot hangs on shutdown here, so the report is the signal and the process is
# reaped either way. Give it a moment to exit on its own first.
if (-not $process.HasExited) {
    $process.WaitForExit(3000) | Out-Null
}

$hung = -not $process.HasExited
if ($hung) {
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(5000) | Out-Null
}

$log = @()
foreach ($file in @($logPath, "$logPath.err")) {
    if (Test-Path $file) { $log += Get-Content $file }
}
if (Test-Path "$logPath.err") {
    Add-Content -Path $logPath -Value (Get-Content "$logPath.err")
    Remove-Item "$logPath.err" -Force
}

$errors = $log | Where-Object {
    $line = $_
    ($line -match "ERROR|SCRIPT ERROR|Unhandled exception|Cannot open") -and
    -not ($knownExitNoise | Where-Object { $line -like "*$_*" })
}

$failures = @()

if (-not (Test-Path $reportPath)) {
    $failures += "the game never wrote its report"
} else {
    Write-Host ""
    Write-Host "=== Smoke report"
    Get-Content $reportPath | ForEach-Object { Write-Host "  $_" }
    $report = Get-Content $reportPath

    if (-not ($report -contains "invariants=ok")) {
        $failures += "the report does not confirm the world invariants"
    }

    if ($Combat) {
        if (-not ($report -contains "combat_evidence=True")) {
            $failures += "the combat smoke run retained no resolution evidence"
        }
        if (-not ($report | Where-Object { $_ -like "combat_presentations=*attack.*" })) {
            $failures += "the combat smoke run selected no attack presentation"
        }
        if (-not ($report -contains "combat_player_impacts=1")) {
            $failures += "the combat smoke run resolved no player impact"
        }
        if (-not ($report -contains "combat_npc_impacts=1")) {
            $failures += "the combat smoke run resolved no NPC impact"
        }
    }

    $cycles = ($report | Where-Object { $_ -like "sort_cycles=*" }) -replace ".*=", ""
    if ($cycles -and $cycles -ne "0") {
        $failures += "the volume sorter reported $cycles cycles"
    }

    if ($SaveLoad) {
        $saveLine = ($report | Where-Object { $_ -like "save=*" }) -replace "^save=", ""
        $landed = ($report | Where-Object { $_ -like "save_landed=*" }) -replace "^save_landed=", ""
        $loadLine = ($report | Where-Object { $_ -like "load=*" }) -replace "^load=", ""

        if ($landed -eq "True") {
            if ($loadLine -notlike "Loaded the world*") {
                $failures += "the in-game load did not adopt the save: '$loadLine'"
            }
        } elseif ($loadLine -notlike "skipped:*") {
            # The trap this guards: a save that never landed leaves an earlier
            # run's file at the save path, so loading it would swap in a world
            # this run never wrote and then confirm *its* invariants — a pass
            # that measured the wrong thing.
            $failures += "the save never landed but the run loaded anyway: '$loadLine'"
        }

        # -Hold keeps an object in transfer for the whole run, so the save
        # boundary is meant to defer and the load is meant to be skipped. Any
        # other save/load run must actually write something.
        if (-not $Hold -and $landed -ne "True") {
            $failures += "the save never reached the disk: '$saveLine'"
        }
    }

    if ($DragDrop -and -not $Hold) {
        $dropLine = ($report | Where-Object { $_ -like "drop=*" }) -replace "^drop=", ""
        if ($dropLine -notlike "Put * in *") {
            $failures += "the in-game drag did not complete: '$dropLine'"
        }
    }

    if ($Screenshot) {
        if (Test-Path $screenshotPath) {
            Write-Host "  saved $screenshotPath"
        } else {
            $failures += "a screenshot was requested but none was written"
        }
    }
}

if ($errors) {
    $failures += "the log contains $($errors.Count) error line(s)"
    Write-Host ""
    Write-Host "=== Errors in $logPath"
    $errors | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
}

Write-Host ""
if ($hung) {
    # A healthy run exits by itself within a second of quitting. Still being
    # alive usually means a modal dialog is waiting for a click nobody can give
    # it — which is exactly how the GodotSharp misconfiguration above presents.
    Write-Host "Note: Godot did not exit after the run and was killed. If this repeats,"
    Write-Host "      launch it windowed ($($MyInvocation.MyCommand.Name) -Windowed) and"
    Write-Host "      look for a modal error dialog."
}

if ($failures.Count -gt 0) {
    Write-Host "--- FAILED: $($failures -join '; ')."
    Write-Host "    Full log: $logPath"
    exit 1
}

Write-Host "The game ran, played $Frames frames, and its world invariants hold."
exit 0
