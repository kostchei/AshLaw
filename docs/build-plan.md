# U8_Ash Build Plan — Local Single-Player Desktop

**Version:** Draft 0.1
**Date:** 27 July 2026
**Supersedes the platform-choice section of:** [implementation-plan.md](implementation-plan.md)
**Inputs:** [World/Interaction/UI spec](ultima-viii-class-world-interaction-ui-spec.md), [Ash V1 rules](../vendor/ash-v1-rules/README.md), [rules integration map](../vendor/ash-v1-rules/INTEGRATION.md)

---

## 0. Decision and scope

**Target:** a local, offline, single-player Windows desktop build. No netcode, no server,
no accounts. The spec's §1.3 exclusion of networked multiplayer now holds as written,
and §13 save/load is a real save file again.

**Scope discipline retained from the platform review:** the simulation stays a headless
module with no renderer, input, or Godot dependency, and all mutations go through an
explicit command/event API. This is *not* speculative multiplayer plumbing — it is what
makes the Appendix A invariants testable in CI without booting a window, and it is the
single highest-leverage structural decision in this plan. If multiplayer is ever
revisited it becomes a transport swap rather than a rewrite, but nothing in this plan is
built for that eventuality.

### 0.1 Verified toolchain on this machine

| Component | Found | Use |
|---|---|---|
| Godot | `4.7-stable-mono` (winget, `GodotEngine.GodotEngine.Mono`) | Renderer, windowing, input, audio, editor tooling host |
| .NET SDK | 8.0.419, 9.0.315, 10.0.301 | Class libraries and tests |
| .NET runtime | `Microsoft.NETCore.App` 8.0.25 / 9.0.17 / 10.0.9 | — |
| Python | 3.13.5 | Data import/validation scripts, CSV→JSON conversion |
| Git | 2.55.0 | Not yet initialised in `E:\U8_Ash` — do this first |

Target **.NET 10 (`net10.0`)**, the current stable LTS available on this machine.
Godot 4.7 accepts .NET 8 or later, and the complete Godot-hosted solution is verified
against .NET 10. Pin every project to the same TFM; mismatched TFMs across the solution
are a common source of misleading runtime load errors.

### 0.2 Language policy

**C# for everything.** GDScript is permitted only inside `@tool` editor plugins where
its faster iteration genuinely helps, and never for simulation, rules, or content
loading. Mixing the two across the simulation boundary costs marshalling on every call
and destroys the "runs headless under `dotnet test`" property that the whole test
strategy depends on.

---

## 1. Repository layout

```text
E:\U8_Ash\
  Ash.sln
  docs\
    ultima-viii-class-world-interaction-ui-spec.md   (existing)
    implementation-plan.md                           (existing)
    build-plan.md                                    (this file)
    adr\                                             decisions, numbered, one per file
  vendor\ash-v1-rules\                               (existing, treat as read-only)
  src\
    Ash.Core\          handles, fixed-point math, result types, invariant asserts
    Ash.Rules\         pure MERP/d20 resolver           (no Godot, no Ash.Sim)
    Ash.Content\       schemas + loaders + validators   (no Godot)
    Ash.Sim\           world, collision, processes, save (no Godot; -> Core, Rules, Content)
    Ash.Script\        Lua host, intrinsics, sandbox     (no Godot; -> Sim)
  tests\
    Ash.Rules.Tests\      xUnit + rulebook fixtures
    Ash.Content.Tests\    schema/validator tests
    Ash.Sim.Tests\        invariant, fuzz, save round-trip
    Ash.Script.Tests\     intrinsic contract tests
    fixtures\             golden JSON, worked examples, seeded fuzz corpora
  game\                                             the Godot project
    project.godot
    Ash.Game.csproj      -> ProjectReference all of src\
    scenes\  scripts\  shaders\  assets\
  tools\
    shapetool\           shape metadata editor (Godot EditorPlugin)
    maptool\             map editor (Godot EditorPlugin)
    import\              Python: CSV/JSON validation, asset conversion
```

**Dependency rule, enforced by a CI check that greps for forbidden `using` directives:**
nothing under `src\` may reference `Godot`. `game\` may reference everything. Rules may
not reference Sim. This one check is what keeps the simulation testable.

---

## 2. Foundational technical decisions

These are cheap now and extremely expensive at M4. Each should get an ADR in `docs\adr\`.

### 2.1 Integer world coordinates

Decision record: [ADR 0001](adr/0001-integer-world-coordinates.md).

World position is `int32` in **world units**, with `1 tile = 256 units` and `1 vertical
level = 8 units` (tune the constants, keep them powers of two). Velocities are
units-per-tick. No floats in `Ash.Sim` — anywhere.

This gives exact determinism (SCR-004, §2.5) for free, makes save files byte-stable,
makes collision arithmetic exact, and eliminates the entire class of "object drifts one
unit per save/load cycle" bugs. Floats appear only at the projection boundary in
`Ash.Render`, converting world units to screen pixels.

Ultima VIII itself used integer map coordinates; this is not a novel risk.

### 2.2 Fixed-tick simulation

The simulation advances at a fixed **60 ticks/second** via an accumulator in the Godot
`_Process` loop. Rendering interpolates between the previous and current tick for smooth
motion; the simulation never sees a variable delta. A tick takes a `TickInput` struct
and returns a list of `WorldEvent`s.

Consequence: the entire game loop from spec §3.2 steps 1–6 lives inside
`World.Tick(TickInput)` in `Ash.Sim`, and steps 7–10 live in `game\`. A headless test
can drive 100,000 ticks in seconds.

### 2.3 Object handles

```csharp
public readonly struct ObjectId {          // 32-bit: 24-bit index, 8-bit generation
    private readonly uint _bits;
    public int Index      => (int)(_bits & 0x00FF_FFFF);
    public byte Generation => (byte)(_bits >> 24);
    public static readonly ObjectId None = default;
}
```

Destroying an object bumps its slot generation. Any stale handle therefore fails a
generation check on dereference. Per project convention this **throws** — `WRLD-005`'s
"fail safely" means a defined, loud failure, not a silent null. Scripts get a separate
`TryResolve` path that returns a script-visible nil, because a content author's dangling
reference is a content bug to report, not an engine crash.

24 bits caps a map at ~16.7M live objects. Acceptable.

### 2.4 Location as a tagged union

```csharp
public enum LocationKind : byte { OnMap, InContainer, Equipped, InTransfer }

public readonly struct Location {
    public LocationKind Kind;
    public ushort   MapId;      // OnMap
    public Vec3i    Position;   // OnMap
    public ObjectId Parent;     // InContainer / Equipped
    public byte     Slot;       // Equipped
    public uint     TransferId; // InTransfer
}
```

Exactly one `Location` per object, stored in one array. Appendix A invariants 2 and 11
become structurally unrepresentable rather than merely tested. **There is no
`MapPosition` field on a contained object** — a contained object's position does not
exist, and code that wants one must ask for its root container's position explicitly.
Resisting the temptation to keep a stale `x,y,z` "for convenience" is the whole point.

### 2.5 Depth sorting — the highest-risk subsystem

Decision record: [ADR 0002](adr/0002-volume-graph-depth-sorting.md).

The U8 sort predicate operates on `SortItem` volumes:

```text
footprint {x_min, x_max, y_min, y_max}   vertical {z_min, z_max}
```

**The predicate is not a strict weak ordering.** Objects A, B, C can form a cycle
A-behind-B-behind-C-behind-A. This is why feeding it to `List.Sort()` produces the
frame-to-frame flicker that GFX-052 forbids — `Sort` is undefined behaviour on a
non-transitive comparator and will happily reorder identical input.

Approach:

1. Port the pairwise `Below(a, b)` predicate from ScummVM's
   `ultima8/world/sort_item.h` (`SortItem::below`) — it is the behavioural reference the
   spec names in §18, and it encodes years of special cases (flat items, sprites, fixed
   vs. dynamic, the "occludes" fast path) that are not worth rediscovering. Porting it is
   licensed by [ADR 0005](adr/0005-gplv2-and-reuse-over-reimplementation.md): this project
   is GPLv2, matching ScummVM. Ported code carries an attribution comment naming the
   upstream file.
2. Build a DAG over the visible set from pairwise comparisons and **topologically sort
   with explicit cycle detection**.
3. On a detected cycle, break it deterministically on a stable key
   `(x_min + y_min, z_min, ObjectId)` and **emit a diagnostic** consumed by the map tool
   (GFX-054). Never break it silently.
4. Allow per-shape explicit sort bias from metadata (GFX-055).

Cost control is pragmatic rather than a milestone gate. Bucket the visible set by
screen-space grid and avoid obviously irrelevant comparisons, but do not delay playable
features for synthetic object-count or frame-time targets. Profile and optimise only
when a real scene on supported hardware demonstrates a player-visible problem.

### 2.6 Palette pipeline

Shapes are stored as **8-bit index textures** (`Image.Format.R8`), with a separate
`256×1 RGBA8` palette texture. A fragment shader does the lookup:

```glsl
// index texture and palette texture BOTH must be nearest-filtered, no mipmaps, no repeat
float idx = texture(index_tex, UV).r;              // 0..1, quantised to i/255
vec2  pal_uv = vec2((idx * 255.0 + 0.5) / 256.0, 0.5);
vec4  c = texture(palette_tex, pal_uv);
if (idx < 0.5/255.0) discard;                      // index 0 is the transparency key
COLOR = c;
```

The `+0.5/256.0` half-texel offset is mandatory; without it, indices land on texel
boundaries and half the palette samples the neighbouring entry. Verify with a 256-entry
identity test image at M1 — this is a five-minute test that catches an otherwise
maddening bug.

- **Fades, tints, damage flashes, inversion (GFX-041/042):** mutate the 256-entry
  palette on the CPU and re-upload. It is 1 KB per frame; the cost is irrelevant.
- **Cycling (GFX-043):** rotate a declared index range within the palette image. Declared
  per-palette in content data as `{start, count, ticks_per_step}`.
- **UI safety (GFX-044):** reserve a fixed index range (e.g. 240–255) for gump chrome and
  exclude it from every global transform, so a world fade cannot corrupt UI colours.

### 2.7 Sprite rendering

One `CanvasItem` draws the whole world in `_Draw()` via ordered
`DrawTextureRectRegion` calls, in the order the sorter produced. Immediate-mode drawing
into a single canvas item gives exact, total control over painter order — which is
precisely what the sorter's output requires and what any scene-tree-based Z ordering
would fight.

If profiling at M1 shows draw-call overhead dominating, the escape hatch is a
`MultiMesh` with per-instance ordering in buffer order, and a texture atlas per shape
archive. Do not build that first.

### 2.8 Viewport and scaling

- Project settings: `display/window/stretch/mode = viewport`,
  `stretch/aspect = keep`, `stretch/scale_mode = integer`, base resolution **640×400**.
- The initial Windows window is **1280×800**, an exact 2× display scale.
- Every texture: `Nearest` filtering, mipmaps off.
- Gumps render **inside** the same 640×400 viewport — they are pixel art, not modern UI.
  An optional high-legibility text mode (UI-054) renders a second text pass at native
  resolution, and is the only thing permitted outside the logical canvas.

Godot's viewport stretch mode already maps mouse input into logical coordinates, which
covers most of GFX-003/005 and UI-012 for free. **Write the test anyway** at M1: assert
`ScreenToLogical(LogicalToScreen(p)) == p` at 1×, 2×, 3×, 4×, fullscreen, and windowed.

### 2.9 Scripting host

Decision record: [ADR 0003](adr/0003-moonsharp-object-scripting.md).

**MoonSharp** (pure-C# Lua, no native dependency, sandboxable) for object scripts.

Rationale: Lua coroutines *are* the cooperative process model from spec §10.3 — a
`coroutine.yield` on a wait condition is a one-line mapping to the process kernel, where
any other option requires building a scheduler and a state machine by hand. Content
authors get hot-reloadable text files, satisfying M5's "no engine-code changes" exit
proof.

**Known limitation, addressed head-on:** MoonSharp coroutine state cannot be serialised.
PROC-006 explicitly permits reconstruction at defined safe points, so a saved process
persists as `{script_id, entrypoint, args, wait_condition, resume_label, local_state}`
and resumes from a declared label. Scripts must therefore be written to declare their
safe points. Enforce this with a script-validation pass in the tooling: a `yield` that is
not at a declared safe point is a **content build error**.

Sandbox: no `io`, no `os`, no `require`, no `load`. Only the intrinsic table. An
instruction-count hook aborts a runaway script and throws.

---

## 3. Milestones

Sizing assumes one experienced developer. These are honest and include test-writing.
Total: **roughly 10–13 months to the §15 acceptance slice**, excluding art and audio
production, which must run in parallel from M2 once the shape tool exists.

Each milestone's exit proof is a CI-runnable check. "It looks right" is never an exit.

---

### M0 — Rules service (2–3 weeks) · parallelisable · zero engine risk

Implements INTEGRATION.md §10 steps 1–12. No Godot, no world model.

**Tasks**

1. `Ash.Rules` project; enums for `AttackCategory`, `ArmorType`, `CritTable`, `CritTier`,
   `ConditionType`, `DamageType`.
2. Loader for `vendor/ash-v1-rules/data/combat_system_data.json`. Validate strictly:
   unknown keys, missing armour rows, and out-of-range values all **throw** at load.
3. Loader for the four `ct_*.csv` trauma tables into `[tier, index] -> TraumaEntry`.
4. **Cross-source consistency check.** Load `attack_tables_summary.csv` as well and
   assert it matches the JSON for every `(category, armour)` triple. It currently does
   **not** — see §5.1. The loader must throw on disagreement rather than pick a winner.
5. Trauma prose → structured effects, **parsed once at import**, never at runtime:
   `+N hits`, `-N activity`, `stunned N rounds`, `prone`, `limb_disabled{slot}`,
   `item_broken{slot}`, `bleed N/round`, `death`. Any trauma line the parser cannot fully
   account for is a build error, not a silently-dropped effect.
6. `AttackRequest` / `AttackResult` records exactly as INTEGRATION.md §3–4 define them.
7. `static AttackResult Resolve(in AttackRequest r)` — pure, allocation-free, no RNG
   inside (the caller supplies `raw_d20`).
8. Class progression: the published Lv 1–30 table is authority. Encode it as data, derive
   it from the bracket rules in a test, and assert they agree.
9. Spellcasting constraints: armour restriction by tradition, cleric virtue thresholds,
   natural-1 mishap lockout.

**Exit proof**

- `combat_engine_rules.md` §5 Scenario 1 (Greataxe vs Plate → 7 hits, no crit) and
  Scenario 2 (tentacle vs Leather → 25 hits, Tier C, index 7) pass as fixtures.
- `critical_hits_system.md` §4 walkthrough passes as a fixture.
- 10,000-case golden-file test: identical input → byte-identical output across runs.
- Editing a threshold in the vendored JSON changes results with zero code changes.

---

### M1 — Visual substrate (3–4 weeks) · spec Stage 1

**Tasks**

1. Godot project in `game\`; solution wiring; the no-`using Godot` CI check.
2. 640×400 viewport, 1280×800 initial window, integer scaling, letterbox, nearest
   filtering everywhere.
3. Palette shader per §2.6 + identity-image verification test.
4. Palette controller: fade to/from colour, global tint, index-range cycling, reserved UI
   range.
5. Shape resource format: multi-frame, per-frame `{w, h, origin_x, origin_y, duration}`,
   per-frame 1-bit alpha mask for hit testing, per-shape `{footprint, height, flags,
   sort_bias}`. JSON metadata beside a PNG/index-texture atlas.
6. Oblique projection `Vec3i -> Vector2` and its inverse, both in one file, both tested
   for round-trip.
7. Camera: free scroll, follow-target, script-detach, deterministic under fixed tick.
8. Sorter per §2.5 including cycle detection and diagnostics.
9. Sprite draw pass per §2.7.
10. Debug overlay: sprite bounds, footprints, sort index, projected origin.

**Exit proof**

- Camera scrolls a hand-built multi-elevation test map interactively.
- **Stability:** a fixed camera path over 10,000 frames produces zero changes in relative
  sort order for any object pair (assert on the emitted order list, not pixels).
- **Round-trip:** `ScreenToLogical(LogicalToScreen(p)) == p` at 1×–4×, windowed and
  fullscreen.
- Palette identity test: all 256 indices map to the exact authored RGB.

---

### M2 — Object world (4–5 weeks) · spec Stage 2

**Tasks**

1. `Ash.Sim` object store: parallel arrays, generational handles, `Location` union.
2. Map container, region activation (WRLD-012/013), uniform-grid spatial index keyed on
   footprint spans, rebuilt on commit only.
3. Spatial queries: point, volume, ray, by-capability, by-region.
4. Collision volumes and the movement solver: sweep, identify the *specific* blocker
   (SIM-002), never leave an object embedded (SIM-006).
5. Support resolution: highest valid support under a footprint; supported-by back-links.
6. Gravity and falling processes; fall interruption; hazard surfaces.
7. Save v1: full deterministic snapshot, versioned header, integer-only payload.
8. **Shape metadata tool** (`tools\shapetool`) — origins, footprints, heights, flags,
   animation groups, sort bias. Art production unblocks here.

**Exit proof**

- Headless: place, stack, move, drop, and reload 1,000 objects; all invariants hold.
- Unsupported objects fall to the highest valid support, always.
- `save → load → save` is byte-identical.
- Every Appendix A invariant is an assertion that runs at every commit in debug builds.

---

### M3 — Direct manipulation (3–4 weeks) · spec Stage 3

**Tasks**

1. **One** transactional transfer service covering map ↔ container ↔ equipment.
   Two-phase: validate fully, then commit atomically. A failed validation mutates
   nothing (INT-013, SIM-031).
2. Containers: nesting, acyclicity enforcement, capacity/weight/type restrictions,
   destruction content policy (OBJ-006).
3. Quantities: merge, split, partial transfer, consume-to-zero.
4. Equipment slots referencing ordinary objects; equip validation; derived-stat
   recomputation from authoritative equipment only.
5. Verbs `Look` and `Use`; sprite-aware hit testing using the M1 alpha masks; reach
   validation.
6. Drag/drop state machine with a single authoritative source throughout.

**Exit proof**

- **Identity round-trip:** one object goes map → nested container → inventory →
  equipment slot → map, retaining `ObjectId`, quality, and quantity.
- **Fuzz:** 10,000 randomised transfers with injected failures at every validation step
  never violate Appendix A invariants 1, 2, 6, 7, or 11.
- Every failure path leaves the object at exactly one valid location.

---

### M4 — Avatar and foundational UI (5–6 weeks) · spec Stage 4

**Tasks**

1. Directional animation state machine: idle, turn, walk, run, jump, fall, land, attack,
   cast, hit, die.
2. Locomotion on the shared movement solver — direct and path-assisted movement must
   produce identical collision results (MOVE-007), asserted in a test.
3. Jump arcs (deterministic, integer), fall transitions, landing resolution.
4. Moving supports: carry rules, clean detach, save/load coherence.
5. Camera follow through vertical motion (GFX-023).
6. Gump layer: z-order, focus, modal routing, drag routing, the §11.2 input priority
   stack.
7. Gumps: container, inventory, paper doll, status, system menu, message box.
8. Cursor state machine per UI-010.

**Exit proof**

- The avatar traverses a multi-elevation test area using only normal UI and movement.
- **Input replay:** a recorded input sequence over a modal gump never produces a single
  world mutation.
- Gump hit testing agrees with gump z-order at every scale.

---

### M5 — Script and process (4–5 weeks) · spec Stage 5

**Tasks**

1. `Ash.Script`: MoonSharp host, sandbox, instruction-count guard.
2. Event dispatch for the full §10.1 event list, with source/target/actor context.
3. Process kernel: coroutine-backed, owner-linked, `on_owner_destroyed` policy per
   process type, incompatible-process replacement policy (PROC-005).
4. Wait primitives: time, animation-complete, movement-complete, speech-complete,
   UI-dismissed, process-complete, condition, transition.
5. Bounded intrinsic API per §10.2, versioned, validating every handle and argument.
6. Triggers/eggs: enter, exit, proximity, use, collision, timer, explicit; persistent
   activation state.
7. Global flags and object-local state.
8. Dialogue shell + dialogue gump; bark text.
9. Scripted camera and cutscene sequencing with explicit, reversible input suppression.
10. **Script validation tool**: symbol resolution, safe-point checking (§2.9), missing
    object/map/dialogue-node detection.

**Exit proof**

- The spec §10.4 locked-door sequence plus a key, an NPC, a trigger, and a cutscene are
  authored **entirely in content**, with zero engine-code changes.
- Save mid-cutscene → reload restores valid camera, input, UI, and process state.
- A deliberately broken script produces a content build error with a source location,
  not a runtime crash.

---

### M6 — Actors and combat (6–8 weeks) · spec Stage 6 — *first consumer of M0*

**Tasks**

1. Actor state per §8.1; faction/disposition; condition system with the
   `{type, source, magnitude, duration, stacking, removal, presentation_key}` record from
   INTEGRATION.md §7.
2. **Rounds → ticks conversion as a single configurable constant** (see §5.3).
3. Behaviour profiles: idle, loiter, patrol, scheduled, converse, investigate, pursue,
   attack, flee, scripted, incapacitated, dead. Four distinguishable enemy profiles.
4. Pathfinding derived from the **shared spatial query** — never a parallel nav grid.
   This is the specific control for the pathfinding-drift risk in spec §20.
5. Schedules with defined fallback on unreachable destination (AI-014).
6. Attack process per INTEGRATION.md §8: validate → animate → at the action frame build
   `AttackRequest` → `Ash.Rules.Resolve` → apply atomically → spawn effect processes →
   emit presentation → persist.
7. Derived `AttackModifier` / `DefenseModifier` services reading equipped objects, with
   the armour DEX penalty and STR compensation from `combat_engine_rules.md` §1.1.
8. Projectiles as tracked objects; melee hit windows; armour from equipment.
9. Damage, death, corpse, loot, resurrection policy.
10. Spells: targeting modes, costs, reagents, restrictions, provocation, natural-1
    mishaps.
11. Combat log reproducing the full resolution trace.

**Exit proof**

- Player and AI attacks route through **one** call site into `Ash.Rules` — asserted by a
  test that stubs the resolver and counts entry points.
- A critical result produces structured damage + conditions + narrative text coherently.
- Combat results survive save/load.
- Four actor profiles are behaviourally distinguishable in an automated scenario.

---

### M7 — Acceptance content (8–12 weeks) · spec Stage 7

Settlement, several interiors, one multi-level hazard area, six+ NPCs with dialogue,
25+ movable props, doors/locks/keys/switches/triggers, a teleport or map transition, a
branching quest with physical world consequences, a readable clue required for
progression, a cinematic, audio, full persistence.

**Exit proof:** spec §15.1 steps 1–12 completed from a clean start with no debug
intervention, scripted as an automated playthrough wherever the input is deterministic.

---

### M8 — Hardening (4 weeks) · spec Stage 8

Sort-cycle diagnostics surfaced in the map tool, save migration, cancellation edge cases
(drag, targeting, quantity, dialogue, cutscene, map transition), performance passes,
input scaling tests, accessibility options, content validation in CI, Windows export
pipeline and packaging.

**Exit proof:** no invariant fails under a soak test of repeated play, save/load,
cancellation, and map transition.

---

## 4. Cross-cutting practices

### 4.1 Error policy

Per project convention, **no silent fallbacks**. Distinguish two categories explicitly in
code and never conflate them:

- **Engine/content errors** — invalid handle, missing shape, cyclic container graph,
  partial transfer, unparseable trauma line, cross-source data disagreement. These
  **throw**. Debug builds also assert every Appendix A invariant at commit boundaries.
- **Validated player rejections** — out of reach, container full, illegal equip slot,
  blocked movement. These are *not* errors. They return a typed `Rejection` with a
  reason code, drive UI-080..082 feedback, and mutate nothing.

A `catch` that swallows and continues is a code-review failure.

### 4.2 Testing

| Layer | Method |
|---|---|
| `Ash.Rules` | xUnit, rulebook worked examples as fixtures, golden files |
| `Ash.Sim` | xUnit, headless tick-driving, invariant asserts, seeded fuzz corpora |
| `Ash.Content` | schema validation, round-trip, deliberate-corruption tests |
| `Ash.Script` | intrinsic contract tests, sandbox escape attempts, safe-point validation |
| Sorting | assert on the **emitted order list**, not pixels — this is pure data |
| Projection / input | round-trip assertions at every scale |
| Rendering | a small, manually-reviewed screenshot set only; do not build a pixel-diff harness |

The sorter and projection being pure functions over data is what makes the riskiest
subsystem testable without a window. Keep them that way.

### 4.3 Content pipeline

All content is JSON validated against a schema at load, with unknown fields rejected. A
`content build` step (Python, `tools\import`) validates every map, shape, script, and
dialogue tree and fails the build on a broken reference. Content errors must be caught at
build time, never at play time.

### 4.4 Determinism harness

From M2 onward, keep a record/replay harness: capture `TickInput` per tick, replay it,
assert the resulting world hash is identical. This catches float leakage, iteration-order
dependence on hash containers, and uninitialised state — the three things that silently
destroy reproducibility and are close to undebuggable once content exists.

---

## 5. Open items to resolve before the milestone they block

### 5.1 AT-3 / AT-4 data conflict — RESOLVED 2026-07-27

The JSON and markdown carried reversed damage curves for AT-3 (2-Handed) and AT-4
(Missile); the CSV was correct. Corrected against the published MERP tables and
recorded in [PROVENANCE.md](../vendor/ash-v1-rules/PROVENANCE.md). The CSV is now a
generated artifact.

**The lesson that generalises:** MERP's curve does not run the way intuition suggests.
Armour *lowers* the hit threshold and *flattens* the multiplier — a solid blow on plate
always registers something small, while an unarmoured target takes nothing until a real
hit lands and then takes catastrophic damage. Anyone tuning these tables, or fitting the
still-unvalidated AT-7/AT-8/environmental rows, must not "fix" a table toward the
intuitive direction. Validate against source, not against expectation.

M0 task 4 stands, generalised: the loader validates JSON against
`tools/import/merp_reference.json` and throws on any column outside tolerance, and CI
runs `gen_attack_tables.py --check` to catch a stale CSV.

### 5.1a Unvalidated attack tables — partially resolved 2026-07-29

AT-7 (Spell Bolts) and AT-8 (Spell Balls) are now transcribed and validated across
their usable ranges in `docs/reference/merp-attack-tables.csv`; the reproducible
full-shape check is `tools/import/analyze_attack_table_shape.py`.
`environmental_fall_crush` still has no source table and remains unvalidated.

### 5.1b Worked example in `combat_engine_rules.md` §5 Scenario 2 — RESOLVED 2026-07-29

The "Giant Squid Tentacle vs Leather" example used threshold 8, multiplier 1.8, base
crit 17, interval 2 — a combination matching **no** table in the package. It has been
explicitly retired in the rulebook and is not an acceptance fixture. The rulebook now
shows the likely intended AT-6 Leather calculation using the fitted curve
(Hit 14, Origin 12, Linear 0.553, Quadratic 0.056), yielding 11 hits,
Tier A, index 7 and the authoritative CT-4 CSV outcome.

### 5.1d MERP → d20 conversion — RESOLVED 2026-07-28

The vendored rules are a d20 re-fit of MERP's d100 tables. **No percentile dice anywhere
in the engine.** The scale factor is:

```text
net_roll = merp_d100_roll / 5          MERP row 146-150  <->  net roll 30
```

This is confirmed empirically, not assumed: it is what makes the fitted curves land
within 1.5 hits of the published MERP damage across all 24 validated armour columns
(§5.1). The class caps corroborate it — Fighter +20 = +100 OB in MERP terms, Cleric +15
= +75, Rogue +10 = +50, Wizard +6 = +30.

Three MERP mechanics do not survive the conversion. All three are deliberate:

| MERP mechanic | Decision | Consequence to accept |
|---|---|---|
| **Open-ended rolls** (96-00 rolls again and adds) | **Ignored for now.** Deferred, not rejected | Max net roll is hard-capped at `20 + AttackMod - DB`. MERP's fat lethality tail is gone; outcome spread becomes a function of the level gap rather than a lottery. Reasonable for real-time, where unseen dice make spiky one-shot deaths feel arbitrary |
| **Weapon fumbles** (`UM 01-08 Possible Fumble`, `UM 01-02 Attack Failure` on AT-5/AT-6) | **Not implemented.** No weapon fumble mechanic | Only the natural-1 spell mishap remains. A missed swing is just a miss |
| **Independent d100 critical roll** | Replaced by single-roll indexing, `raw_d20 % 10` | Verified non-issue: across all tables and a realistic modifier range, all 10 trauma indices reach all 5 severity tiers (50/50 combinations per table). Coupling only persists within a single frozen matchup, and Tier E saturating at 4+ means several raw rolls reach E at different indices |

The current fit is measured across every integer d20-scale net result, not only the
final row. Linear curves are used where normalized RMSE is at most 5%; seven AT-5/AT-6
columns use a small quadratic term to retain their convex shape. Hit threshold and
damage-curve origin are separate, so first damage and high-end slope no longer compete.

Granularity, not variance, is what the conversion costs. A d20 spans MERP 5-100 in
5-point steps, so die swing is preserved but the smallest resolvable step is 5 MERP
points. Do not model MERP-scale effects smaller than that; they round away.

**M0 must encode the scale factor as a named constant with the row-146-150 check as a
fixture**, so a future tuning pass cannot quietly drift it.

### 5.1c Armour column count — decide before M6

MERP's tables have **five** armour columns: Plate, Chain, Leather-Rigid, Leather-Soft,
None. The engine models four, collapsing the two leather columns to their arithmetic
mean (this is what `merp_reference.json` records and what the validator checks). Rigid
and soft leather diverge meaningfully on some tables — AT-1 caps at 22 vs 25, AT-5 at
32 vs 36 — so if leather armour is meant to be a progression rather than one item
class, the fifth column should be restored before equipment data is authored.

The collapse and its reversal cost are recorded in
[ADR 0004](adr/0004-armour-column-collapse.md).

### 5.2 Concussion-hit vitality model — blocks M6

The vendored rules define an accumulating "hits" pool with no stated maximum, no per-actor
capacity, and no defined death threshold outside the Tier D/E trauma text. Actor vitality,
the unconscious/dying boundary, and healing rates all need defining before combat can be
tuned.

### 5.3 Rounds → real time — blocks M6

The rules are turn-based ("stunned 2 rounds", "bleed per round"); the engine is real-time
(CMB-001..004). One configurable constant, defined once, per INTEGRATION.md §7. Pick it
early — it sets the felt lethality of the entire game, and every trauma duration in the
CSVs inherits it.

### 5.4 Art production — blocks M7, unblocked by M2

Original 256-colour pixel art with per-frame origins and physical metadata is a
substantial commission: terrain, structures, 25+ props, six+ NPCs with directional
animation sets, effects, and gump chrome. It cannot start before the shape tool at M2 and
must run in parallel from that point. This is the most likely cause of M7 slipping.

### 5.5 Audio — blocks M7

Music, ambient loops, positional world effects, and barks (AV-001..007). Same parallel
track as art.

---

## 6. Status — M0 complete, M1 entry

### 6.1 M0 completion checklist

All twelve M0 tasks and all four exit proofs are done. See §M0 for the task text.

| # | M0 task | Status | Where |
|---|---|---|---|
| 1 | `Ash.Rules` project and combat enums | Done | `src/Ash.Rules/CombatModels.cs` |
| 2 | Strict JSON loader; unknown keys and out-of-range values throw | Done | `RulesDataLoader.cs` |
| 3 | Four `ct_*.csv` trauma tables into `[tier, index]` | Done | `RulesDataLoader.LoadCriticalTables` |
| 4 | Cross-source consistency check against `attack_tables_summary.csv` | Done | `RulesDataLoader.ValidateAttackSummary` |
| 5 | Trauma prose → structured effects, unaccounted text is a build error | Done | `TraumaEffectParser.cs` |
| 6 | `AttackRequest` / `AttackResult` per INTEGRATION.md §3–4 | Done | `CombatModels.cs` |
| 7 | Pure `Resolve`, no RNG inside | Done | `AttackResolver.cs` |
| 8 | Class progression as data, derived from bracket rules in a test | Done | `class_progression.csv`, `ClassProgressionTests.cs` |
| 9 | Spellcasting constraints: armour, cleric virtues, natural-1 lockout | Done | `SpellcastingConstraints.cs` |
| — | Scale factor as a named constant with the row-146-150 check (§5.1) | Done | `merp_reference.json`, `analyze_attack_table_shape.py` |

| Exit proof | Status | Where |
|---|---|---|
| §5 Scenario 1 (Greataxe vs Plate) as a fixture | Done — 8 hits, not the published 7; see below | `greataxe-vs-plate.json` |
| §5 Scenario 2 (tentacle vs Leather) as a fixture | Retired and replaced | `grapple-vs-leather-corrected.json` |
| `critical_hits_system.md` §4 walkthrough as a fixture | Recomputed and replaced; see below | `greataxe-vs-chain-critical-walkthrough.json` |
| 10,000-case golden file, byte-identical across runs | Done | `GoldenResolutionTests.cs`, `attack-resolution.golden` |
| Editing a vendored threshold changes results with no code change | Done | `RulesDataLoaderTests.cs` |

**Three published worked examples did not survive contact with the data.** All three
are recorded in `PROVENANCE.md` rather than quietly reconciled:

- §5 Scenario 1 now yields 8 hits, not 7 — an arithmetic consequence of the AT-3 Plate
  correction, not a resolver change.
- §5 Scenario 2 quoted attack parameters and trauma text present in no runtime table.
  Retired; a corrected AT-6 Leather illustration replaces it.
- `critical_hits_system.md` §4 has the same defect: it quotes pre-correction AT-3 Chain
  parameters (threshold 11, multiplier 2.0, base crit A 19) and trauma text found in no
  CT table. Its scenario inputs are kept and every expected value recomputed, which
  moves the result from Tier B to Tier A.

**Two defects surfaced from making unparsed trauma text a build error**, which is the
main argument for having done M0 task 5 strictly rather than leniently:

- `shoulder broken`, `Lower leg broken` and `bone broken` produced no effect at all.
  They are now a `BreakBone` effect.
- An unrecognised `If no <thing>` condition parsed as *unconditional*, so its effects
  would have applied to every target instead of only unarmoured ones.

### 6.2 Carried into later milestones

These are open, deliberately, and each is already recorded in the section named:

| Item | Blocks | Section |
|---|---|---|
| `environmental_fall_crush` has no source chart — deferred and enforced | M6 | §5.1c note, `PROVENANCE.md` |
| Fifth armour column (rigid vs soft leather) | M6 | §5.1c |
| Concussion-hit vitality model, death threshold | M6 | §5.2 |
| Rounds → real-time constant | M6 | §5.3 |
| Open-ended rolls, weapon fumbles, size and bolt caps | M6 | §5.1, audit doc |

### 6.3 M1 entry criteria

M0 is self-contained and has no engine dependency, so M1 does not wait on it. M1 may
start when:

1. CI is green on `main` — the headless job (four test projects, architecture check,
   generated-data check, curve analysis) and the Godot build job. **Met.**
2. The three painful-to-reverse decisions have ADRs: §2.1 integer coordinates, §2.5
   volume-graph sorting, §2.9 MoonSharp. **Met** — `docs/adr/0001`–`0003`.
3. The Godot project builds headlessly from the solution. **Met.**

### 6.4 M1 risk spikes — done, with one budget finding

Both spikes are landed in `Ash.Core` and run in CI. They are headless: no Godot
reference, so the M1 risks are testable without an engine.

**1. Palette identity — passes.** `PaletteShaderModel` replicates the §2.6 fragment
shader's arithmetic in 32-bit float, and `game/shaders/palette.gdshader` is written
against it. All 256 indices resolve to their exact authored RGB.

The test asserts the invariant that actually matters, which is **not** that each index
selects the right texel. Without the `+0.5` half-texel offset the arithmetic still lands
on the correct integer — it just lands *exactly on the boundary* between two texels,
where the result depends on how a given GPU rounds. The test asserts every sample sits
0.5 texels from either boundary, and a companion test asserts that removing the offset
drops that margin to 0.0, so the check demonstrably has teeth.

**2. Sorter — correctness spike complete.** Graph construction, deterministic cycle
handling, stable output, and screen-space candidate filtering are implemented. Synthetic
latency and object-count thresholds are no longer milestone gates. The timed xUnit tests
have been retired; future optimisation is driven by profiling an actual playable scene,
not an adversarial benchmark.

**Also landed: projection and viewport round-trips** (M1 task 6, §2.8).
`ObliqueProjection` and `ViewportScaling` are integer-only, so
`ScreenToWorld(WorldToScreen(p)) == p` and `ScreenToLogical(LogicalToScreen(p)) == p`
hold **exactly** — the tests use no epsilon. The viewport round-trip is asserted over
every logical pixel at 1×–5×, windowed and letterboxed.

### 6.4a Viewport settings — fixed, and now enforced

M1 task 2 was recorded as done but was not. `game/project.godot` carried
`window/stretch/mode="canvas_items"`, and `stretch/aspect` and `stretch/scale_mode` were
absent entirely, so Godot was falling back to `ignore` and `fractional`. Nearest
filtering and the former 320×200 base-resolution settings were internally consistent.
The project now intentionally uses 640×400.

That combination is quietly wrong rather than visibly broken: `canvas_items` scales
drawing commands instead of the framebuffer, giving sub-pixel sprite positions, and
`fractional` lets the pixel grid absorb the slack that should go to the letterbox. The
headless `ViewportScaling` round-trip tests would have kept passing throughout, because
they model the *intended* settings.

All three are now set per §2.8 and `tools/check-project-settings.ps1` asserts them in
the headless CI job. It parses `project.godot` rather than querying the engine, so it
needs no Godot installed and — more importantly — an *absent* key fails rather than
resolving to a Godot default. Both failure modes were verified.

This matters more than a one-off fix, because **the Godot editor rewrites
`project.godot` every time the project is opened.** Applying the pending stash also
restored the editor-generated `config/features`, `[dotnet] project/assembly_name`, and
the `uid://` on `Main.cs`, which the same rewrite had produced.

### 6.5 What M1 should do next

1. Build the playable interaction slice first: a visible controllable character,
   backpack, lootable chests, and killable monsters.
2. Port `SortItem::below` from ScummVM when real authored shapes need its special cases,
   replacing `VolumeSorter.Below` rather than
   extending it. The current predicate implements the geometric core only — the
   fixed-vs-dynamic, occludes-fast-path, and shape-flag cases are absent — and it was
   written from first principles while the licensing question was open. That question is
   settled: [ADR 0005](adr/0005-gplv2-and-reuse-over-reimplementation.md) licenses this
   project GPLv2 and makes reuse the preferred route. Should land before M2 authors map
   content.
3. Grow the slice into M1/M2 infrastructure as the working game demands: shape resource
   format, camera, sprite draw pass, object store, containers, and transactional
   transfers. Palette polish and debug overlays follow playable interaction.
