# U8_Ash

An Ultima VIII-class object world in Godot 4.7 with .NET 10, driven by a vendored
tabletop rules package.

The rules layer resolves combat from a **single d20 roll** — hit, concussion damage,
critical severity, and the trauma-table index all come off the same die. The engine
layer is a real-time isometric object world. The two are kept apart on purpose: nothing
under `src/` may reference Godot, so the rules and simulation stay testable without an
engine, and CI enforces it.

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| .NET SDK | 10.0.301 (pinned in `global.json`, `rollForward: latestPatch`) | Everything |
| Python | 3.13 | The data generator and curve analysis under `tools/import/` |
| PowerShell | Windows PowerShell 5.1, or `pwsh` 7+ | `tools/check-architecture.ps1` |
| Godot | 4.7 | Running the game. Not needed to build or test the libraries |

`Godot.NET.Sdk` restores from NuGet, so `game/Ash.Game.csproj` **builds** without the
Godot editor installed. You only need the editor to run the game.

## Playable interaction slice

The main scene now boots directly into a small playable room:

- move the visible character with **WASD** or the arrow keys;
- press **B** or **I** to open the character's backpack;
- stand next to a chest and press **E** to open it;
- click items to transfer them between an open chest and the backpack;
- stand next to a monster and press **F** or **Space** to attack;
- dead monsters leave lootable remains;
- press **R** to reset the demo.

The presentation is intentionally made from simple drawn shapes. It proves the
character/inventory/container/combat loop before the final shape and art pipeline.
The authoritative interaction state lives in `Ash.Sim.PlayableSliceWorld`, not in
Godot nodes.

## Layout

```text
src/          Headless libraries. No Godot references, ever.
  Ash.Core      Shared primitives: palette, depth sorter, projection, viewport
  Ash.Rules     Combat resolution: loader, attack resolver, trauma parser
  Ash.Content   Content loading
  Ash.Sim       Simulation
  Ash.Script    MoonSharp object scripting
game/         The Godot project. The only place Godot is referenced.
tests/        One test project per src library, plus JSON/golden fixtures
tools/        check-architecture.ps1, and the import/validation scripts
vendor/       The vendored ash-v1-rules package: rulebooks, JSON, CSV tables
docs/         Build plan, ADRs, specs, and attack-chart reference material
```

## Build and test

**Start here.** Reports what this machine can build, and why not:

```bash
pwsh ./tools/preflight.ps1
```

**The everyday command.** Runs exactly what CI's headless job runs — five test projects
and four checks — so green here means green there. It does not touch Godot:

```bash
pwsh ./tools/test-headless.ps1
```

Build everything *including* the Godot project. This is the only command here that needs
the Godot .NET SDK:

```bash
dotnet build Ash.sln
```

### Prefer the scripts over `Ash.sln` for library work

`Ash.sln` includes `game/Ash.Game.csproj`, which needs the Godot SDK. Nothing under
`src/` or `tests/` does. Running library work through the solution couples it to the
engine toolchain for no benefit — and when that coupling breaks, the error points
somewhere else entirely (see Troubleshooting).

A single test project still works fine on its own:

```bash
dotnet test tests/Ash.Rules.Tests/Ash.Rules.Tests.csproj
```

## Troubleshooting

### `NU1201: Project Ash.Core is not compatible with net8.0`

**Cause: the Godot editor rewrote `game/Ash.Game.csproj` and set `net8.0`.** Godot 4.x
defaults to net8.0; every project here targets net10.0, and a net8.0 project cannot
reference net10.0 assemblies.

This error is genuinely misleading. `NU1201` is a NuGet code raised during restore and
its text names the *project references*, never the framework — so it reads like a broken
dependency or an SDK that will not resolve. It is neither. Build plan §0.1 predicted
exactly this: mismatched TFMs are "a common source of misleading runtime load errors."

Diagnose and fix:

```bash
pwsh ./tools/check-target-frameworks.ps1
```

Set `<TargetFramework>` in `game/Ash.Game.csproj` back to match `Directory.Build.props`.
**Do not downgrade the shared libraries to net8.0** — they target net10.0 deliberately,
pinned in `global.json`. This check runs first in CI for the same reason.

### An aggregate `dotnet build`/`dotnet test` fails only at the game project

Two different causes, and they need opposite responses:

1. **`NU1201`** — the target-framework mismatch above. A real bug in the repo. Fix it.
2. **The Godot SDK genuinely will not resolve** — a cold NuGet cache with no network, or
   a sandboxed shell. Not a repo bug. Use `pwsh ./tools/test-headless.ps1`, which never
   needs Godot. `pwsh ./tools/preflight.ps1` tells you which of the two you have.

Neither is a reason to stop testing the libraries.

### Godot is not installed at all

Fine for everything except running the game. `Godot.NET.Sdk` restores from NuGet, so
`game/Ash.Game.csproj` still *builds* without the editor. CI keeps the Godot build in a
separate job so a missing SDK cannot block library testing.

## Export a playable build

Install the official Godot 4.7 Mono export templates once, then run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/export-game.ps1
```

This produces two ignored, local distributions:

- `artifacts/builds/windows-client/AshLaw.exe` is the normal Windows game;
- `artifacts/builds/windows-headless/AshLaw.Headless.exe` is the same simulation
  exported for display-free execution;
- `Run Headless Smoke Test.cmd` launches the headless build for two frames and
  reports its exit status;
- `AshLaw-Windows-Test.zip` and `AshLaw-Windows-Headless.zip` are the portable
  packages. Keep each package's contents together after extraction.

The headless export is currently a verification build, not a network server. The
world simulation runs without a display, but multiplayer and a persistent server
loop have not been implemented.

## Checks

All of these run in CI and should pass before a push. `pwsh ./tools/test-headless.ps1`
runs the whole set for you; they are listed individually here for when you want one.

Assert every project targets the same framework. Run this **first** when anything looks
broken — a mismatch makes every other error misleading:

```bash
pwsh ./tools/check-target-frameworks.ps1
```

Assert no headless project has taken a Godot dependency:

```bash
pwsh ./tools/check-architecture.ps1
```

Assert `game/project.godot` still matches build plan §2.8. `Ash.Core.ViewportScaling`
models those settings, and the Godot editor rewrites `project.godot` every time the
project is opened, so this catches the drift:

```bash
pwsh ./tools/check-project-settings.ps1
```

Assert `attack_tables_summary.csv` still agrees with `combat_system_data.json`. The CSV
is **generated**, never hand-edited; this fails if the two diverged or if someone edited
the CSV directly:

```bash
python tools/import/gen_attack_tables.py --check
```

Regenerate it after a deliberate change to the JSON:

```bash
python tools/import/gen_attack_tables.py
```

Re-fit and report every AT-1..AT-8 damage curve against the transcribed source charts,
asserting each column stays within 5% normalized RMSE:

```bash
python tools/import/analyze_attack_table_shape.py
```

## The golden file

`tests/fixtures/rules/attack-resolution.golden` pins the resolver's output over a
deterministic 10,000-case corpus. It stores a SHA-256 of the full document plus 25
sample lines, so a behaviour change fails loudly instead of drifting.

An ordinary test run never rewrites it. After a deliberate rules or resolver change,
regenerate it explicitly and commit the result:

```bash
ASH_REGENERATE_GOLDEN=1 dotnet test tests/Ash.Rules.Tests/Ash.Rules.Tests.csproj
```

## Working with the vendored rules

`vendor/ash-v1-rules/` is copied from an upstream package, not authored here, and it has
**deliberately diverged** from its source. Read
[`PROVENANCE.md`](vendor/ash-v1-rules/PROVENANCE.md) before changing anything under it —
it records every correction, the reasoning, and the refresh procedure. A refresh that
blindly copies upstream forward will reintroduce fixed defects.

Two things worth knowing before you rely on a number:

- **Three published worked examples are wrong** against the corrected data. Two are
  retired and replaced; the third is recomputed. See build plan §6.1.
- **`environmental_fall_crush` is deferred, not accepted.** It has no source chart, so
  `AttackResolver` refuses to use it unless the caller sets
  `AttackRequest.AllowUnvalidatedTable`.

## Licence

**GPL-2.0.** Full text in [`LICENSE`](LICENSE). See
[ADR 0005](docs/adr/0005-gplv2-and-reuse-over-reimplementation.md) for why, and for what
it commits the project to.

The short version: the specification names Pentagram and ScummVM's Ultima VIII engine as
the behavioural reference, and this project **ports** from ScummVM rather than
reimplementing what it already solves. ScummVM is GPL-2.0-or-later, so this project is
GPLv2 to match. Ported code carries a comment naming the upstream file it came from.

Nothing here uses Ultima VIII's own content — no Origin data files are read, the art is
original, and the rules are an unrelated MERP/AD&D blend. The relationship is one of
genre and behaviour.

Two licensing questions are **open and block public distribution**, both recorded in
ADR 0005: `vendor/ash-v1-rules` carries no licence of its own, and game assets are not
covered by a software licence and need separate terms.

## Where to start reading

The visual contract for new rooms and assets is
[`docs/art-direction.md`](docs/art-direction.md).

1. [`docs/build-plan.md`](docs/build-plan.md) — milestones, risks, and current status
   (§6 for where the project actually is).
2. [`docs/adr/`](docs/adr/) — the five decisions that are expensive to reverse.
3. [`vendor/ash-v1-rules/INTEGRATION.md`](vendor/ash-v1-rules/INTEGRATION.md) — the
   proposed boundary between rules and engine.
4. [`src/Ash.Rules/README.md`](src/Ash.Rules/README.md) — how resolution works.
