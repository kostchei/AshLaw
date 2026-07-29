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

Build everything, including the Godot project:

```bash
dotnet build Ash.sln
```

Build and test only the headless libraries — this is what CI's first job does, and it
does not need the Godot SDK:

```bash
dotnet test tests/Ash.Rules.Tests/Ash.Rules.Tests.csproj
```

Run every test project:

```bash
for p in Ash.Core Ash.Rules Ash.Content Ash.Sim Ash.Script; do dotnet test "tests/$p.Tests/$p.Tests.csproj"; done
```

## Checks

All five run in CI and should pass before a push.

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

1. [`docs/build-plan.md`](docs/build-plan.md) — milestones, risks, and current status
   (§6 for where the project actually is).
2. [`docs/adr/`](docs/adr/) — the five decisions that are expensive to reverse.
3. [`vendor/ash-v1-rules/INTEGRATION.md`](vendor/ash-v1-rules/INTEGRATION.md) — the
   proposed boundary between rules and engine.
4. [`src/Ash.Rules/README.md`](src/Ash.Rules/README.md) — how resolution works.
