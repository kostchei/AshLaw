# U8_Ash Implementation Plan

**Version:** Draft 0.1
**Date:** 27 July 2026
**Inputs:** [World/Interaction/UI spec](ultima-viii-class-world-interaction-ui-spec.md), [Ash V1 rules](../vendor/ash-v1-rules/README.md), [rules integration map](../vendor/ash-v1-rules/INTEGRATION.md)

---

## 1. What is actually being built

Two independent bodies of work meet here:

- **An Ultima VIII-class engine.** A continuous 2D oblique-projected world where every
  visible thing is a persistent object with identity, footprint, elevation, collision,
  support, containment, scripting, and save state. The defining requirements are
  *object coherence* (Appendix A invariants) and *volume-based depth sorting*
  (GFX-050..055), not image quality.
- **A deterministic MERP/d20 rules service.** A pure function
  `AttackRequest -> AttackResult` driven by JSON/CSV data, resolving net roll,
  concussion hits, critical tier, trauma index, and structured conditions.

The rules service is the easy half: it is pure, data-driven, testable, and has no
platform dependencies. Build it first — it de-risks nothing about the engine but it
is cheap, and it gives the combat milestone something real to call.

**A third requirement, networked multiplayer, is not in either document.**
§1.3 of the spec explicitly lists networked multiplayer as *out of scope*, and §13
(persistence) is written for single-player save files. This is the single largest
architectural fork and is resolved in §3 below before any platform choice matters.

---

## 2. Platform decision

**Recommendation: Godot 4 (2D renderer, GDScript + C# for hot paths), with an
authoritative dedicated server running the same simulation code headless.**

**Roblox is the wrong tool for this specific spec** — not because it is weak, but
because the three things the spec makes non-negotiable are the three things Roblox
does not give you, while the one thing Roblox gives you for free (networking and
hosting) is the part of the problem that is well-trodden elsewhere.

### 2.1 Where Roblox breaks against the spec

| Spec requirement | Roblox reality |
|---|---|
| **GFX-040..044** indexed-colour pipeline, palette fades, palette cycling, colour transforms | No custom shader access. Palette cycling and tinting must be pre-baked as separate texture frames or approximated with `ImageColor3` (multiply-only). A true 256-entry LUT pipeline is not expressible. This is a hard failure, not a workaround. |
| **GFX-050..055** volume-based depth sort with stable tie-breaks and explicit overrides | Expressible via per-frame `ZIndex` assignment on `ImageLabel`s, but you are writing thousands of Instance properties per frame from Luau. The GUI renderer is not built for that; expect hundreds of sprites, not thousands. The 3D alternative (textured quads) hands sorting to Roblox's per-object transparency sort, which is exactly the wrong sort. |
| **GFX-033 / INT-002** sprite-aware hit testing that ignores transparent pixels | GUI hit testing is rectangular. Alpha-mask testing must be done manually in Luau against `EditableImage` data every pointer move. |
| **GFX-001..005** fixed 320x200 logical canvas, integer scaling, exact logical/physical pointer transform | Achievable (`ResampleMode = Pixelated`, letterbox frame). This one is fine. |
| **§14** shape-metadata, map, and script authoring tools | Roblox Studio plugins are workable but you are authoring U8 content inside a 3D IDE that has no concept of your object model. |
| Content | The critical tables in `vendor/ash-v1-rules` describe decapitation, severed limbs, crushed skulls, and bleed rates. Roblox moderation restricts realistic gore even under age-rated experiences. This is a live product risk, not a technical one. |
| Distribution | Roblox client only. No Steam, no offline, no owning your own store page, Robux-only monetisation. |

### 2.2 Where Roblox genuinely wins

Replication, session/matchmaking, dedicated server hosting, account identity, and
`DataStore` persistence are all free and mature. If the goal is "get many players into
a shared world with near-zero infrastructure effort", that is worth roughly 6–12
months. Luau coroutines are also an excellent match for the cooperative process
kernel in §10.3.

The trade is straightforward: **Roblox buys you the network stack and costs you the
renderer.** The spec is 90% renderer and world model, and the multiplayer requirement
was not in the spec at all — so paying the renderer for the network is backwards here.

### 2.3 Comparison

| Option | Pixel/palette pipeline | Depth-sort control | Editor tooling | Multiplayer | Verdict |
|---|---|---|---|---|---|
| **Godot 4** | Fragment shader with index texture + 256×1 palette LUT; cycling and fades are one uniform swap | Full — custom `sort volume` topological sort feeding draw order | `@tool` scripts and EditorPlugins give a real map/shape editor inside the engine | ENet + `MultiplayerAPI`, or plain custom RPC over WebSocket. Headless export runs the same sim as the server. You build it; it is well documented. | **Recommended** |
| Bevy / custom Rust + wgpu | Total control, best determinism story, ECS matches §5 cleanly | Full | None — you build every tool in §14 from zero | Bring your own (`renet`, `quinn`) | Best engineering, worst schedule |
| Unity 6 | Shaders fine, 2D sprite pipeline fine | Full via custom sorting | Good, but heavier | Netcode for GameObjects | Viable; no advantage over Godot for a 2D pixel game, and heavier licensing/runtime baggage |
| Web (TypeScript + Pixi/WebGL + Node authoritative server) | Full shader control | Full | None — build from zero | Trivial distribution, easy authoritative server | Strong if instant-play matters more than tooling |
| **Roblox** | Blocked (no shaders) | Constrained + perf-limited | Poor fit | Free and excellent | Only if multiplayer reach outranks the spec |

### 2.4 If Roblox is chosen anyway

It is not impossible, but the spec must be formally amended first:

- Downgrade GFX-040..044 from indexed palette to **pre-baked palette variant sets**
  (one texture set per fade/tint/cycle phase), and accept a fixed number of variants.
- Cap simultaneous dynamic sprites (budget ~500) and drive `ZIndex` from a dirty-set,
  not a full re-sort per frame.
- Implement alpha hit testing over cached per-shape 1-bit masks in Luau, not
  `EditableImage` reads.
- Soften the trauma text and visual gore to pass moderation, while keeping the
  numeric rules intact.

---

## 3. The multiplayer fork (decide before writing code)

Multiplayer is not an additive feature here. It rewrites four sections of the spec:

| Spec area | Single-player as written | Multiplayer consequence |
|---|---|---|
| §3.1 authoritative state | One in-process world | World lives on the **server**; clients hold a replicated read-model plus prediction for their own avatar only |
| §6 interaction pipeline | Resolve → validate → mutate locally | Client resolves *intent*, sends an RPC; **server re-runs steps 2–7 authoritatively**. Client-side effects are provisional until the server commits. INT-021 ("one authoritative source throughout the drag transaction") becomes a server-held drag lock with a timeout |
| §10.3 processes / §12.2 cutscenes | Global pause, input suppression, camera detach | No global pause. Cutscenes become per-client presentation over an unpaused world, or instanced areas |
| §13 persistence | Save file capturing a consistent boundary | Continuously persisted server-side world DB; SAVE-001 "consistent simulation boundary" becomes a transactional write boundary. Per-player save/load disappears |
| Determinism (§2.5) | Nice to have | Load-bearing — reconciliation depends on it. Seeded RNG must be server-owned; SCR-004's "deterministic random services" become a server-side seeded stream per object |

**Scope recommendation:** build single-player-shaped but network-clean from day one —
i.e. the simulation is a headless module with no renderer or input dependency, all
mutations go through a command/event API, and the local game is "a client talking to
an in-process server". Then multiplayer is a transport swap, not a rewrite. Do *not*
try to ship both simultaneously in the vertical slice.

---

## 4. Architecture

Six modules, matching spec §3. Hard dependency rule: arrows point one way only.

```
ash.rules      (pure, no engine deps)      <- vendored JSON/CSV
ash.sim        (headless world + processes) -> depends on ash.rules
ash.content    (maps, shapes meta, scripts) -> data consumed by ash.sim
ash.net        (command/event transport)    -> wraps ash.sim
ash.render     (projection, sort, sprites)  -> reads ash.sim, never writes
ash.ui         (gumps, cursor, targeting)   -> sends commands, reads ash.sim
ash.tools      (editor plugins)             -> reads/writes ash.content
```

`ash.sim` must be runnable headless in a test harness with no Godot scene tree
beyond the bare minimum, so the acceptance invariants (Appendix A) can be asserted
in CI.

### 4.1 Core data structures

- **Object store.** Dense arrays keyed by `stable_id` (u32 generational handle, so
  WRLD-005 "fail safely if target destroyed" is a handle-generation check, not a
  null test). One authoritative `location` enum per object: `OnMap{map,x,y,z}` |
  `InContainer{id}` | `Equipped{actor,slot}` | `InTransfer{txn}`. Enforcing this as a
  single tagged union is what makes Appendix A invariants 2 and 11 structurally
  impossible to violate rather than merely tested.
- **Spatial index.** Uniform grid buckets per map, sized to the largest common
  footprint; objects register in every bucket their footprint spans. Rebuilt on
  commit, never on read (SIM-033).
- **Sort graph.** Per visible-set, build pairwise front/behind relations from
  `{footprint, z-interval}`, topologically sort, break ties on a stable key
  `(min_x+min_y, min_z, stable_id)`. Detect cycles and report them to tooling
  (GFX-054) rather than silently reordering.
- **Process kernel.** Coroutine per process, owned by an object handle, with an
  explicit `on_owner_destroyed` policy per process type (PROC-004). Serialisable
  as `{type, owner, wait_condition, local_state}` — processes that cannot express
  their state that way must resume from a declared safe point (PROC-006).

### 4.2 Error policy

Per project convention: **no silent fallbacks.** An invalid object handle, a missing
shape, a cyclic container graph, or a partially committed transfer throws. Debug
builds assert every Appendix A invariant at commit boundaries; release builds keep
the throws and log them. A failed *player action* is not an error — it is a validated
rejection with feedback (UI-080..082). Distinguish these two in the code explicitly.

---

## 5. Milestones

Each milestone ends with a machine-checkable exit proof. Nothing advances on
"it looks right".

### M0 — Rules service (2–3 weeks, parallelisable, zero engine risk)
Implements INTEGRATION.md §10 steps 1–12.
- Load and **validate** `combat_system_data.json`; reject unknown categories/armours.
- Import the four critical CSVs into indexed lookup tables.
- Pure `resolve_attack(AttackRequest) -> AttackResult`.
- Parse trauma prose **once, at import time**, into structured effects
  (`+N hits`, `-N activity`, `stunned N rounds`, `prone`, `limb_disabled{slot}`,
  `item_broken{slot}`, `bleed N/round`, `death`). Keep the prose for the combat log;
  never reparse at runtime.
- Class progression curves as data tables, not formulas (the published Lv1–30 table
  is authority; derive-and-assert against it in a test).

**Exit proof:** every worked example in the rulebooks passes as a fixture
(combat_engine_rules §5 Scenario 1 & 2, critical_hits_system §4); same input always
yields identical output; changing a JSON value changes results with no code change.

### M1 — Visual substrate (spec Stage 1)
Logical 320×200 viewport, integer scaling, letterbox, palette shader (index texture +
LUT uniform, fade/cycle/tint as LUT mutations), shape/frame resource format with
origins, oblique projection, camera, first depth sort.

**Exit proof:** camera scrolls a hand-built multi-elevation test map; overlapping
sprites are stable across 10,000 frames with zero order changes under a fixed
sequence; `screen_to_object(project(obj))` round-trips at every supported scale.

### M2 — Object world (spec Stage 2)
Object store, location union, spatial queries, footprints and heights, collision
volumes, support resolution, gravity/falling, first save format.

**Exit proof:** headless test places, stacks, moves, drops, and reloads 1,000 objects;
unsupported objects fall to the highest valid support; save→load→save produces
byte-identical output.

### M3 — Direct manipulation (spec Stage 3)
Transactional transfer service (one contract for world/container/equipment), look,
use, drag/drop, nested containers, quantities and splits, equipment slots, failed-drop
recovery.

**Exit proof:** the identity round-trip — one object moves map → nested container →
inventory → equipment slot → map, retaining `stable_id`, quality, and quantity;
every failure path leaves the object at exactly one valid location; fuzz test of
10,000 random transfers never violates Appendix A invariants 1, 2, 6, 11.

### M4 — Avatar and foundational UI (spec Stage 4)
Locomotion with directional animation, walk/run, jump/fall/land, moving supports,
camera follow, gump layer with z-order and modal routing, container/inventory/paper
doll/status/system gumps, cursor states.

**Exit proof:** avatar traverses a multi-elevation test area using only normal UI and
movement; modal input never reaches the world in an automated input-replay test;
UI hit testing agrees with gump z-order at all scales.

### M5 — Script and process (spec Stage 5)
Object event dispatch, cooperative process kernel, waits and notifications, bounded
intrinsic API, triggers/eggs, global and object-local flags, dialogue shell, scripted
camera.

**Exit proof:** the key/door/NPC/trigger/cutscene sequence from spec §10.4 is authored
entirely in content, with zero engine-code changes; saving mid-cutscene and reloading
restores valid camera, input, and process state.

### M6 — Actors and combat (spec Stage 6) — *first point M0 is consumed*
Actor state, behaviour profiles, schedules, pathfinding derived from the shared
spatial query (never a parallel nav grid), melee timing windows, projectiles, damage
application, conditions, death/corpse/loot, spells.

**Exit proof:** player and AI attacks demonstrably route through one call site into
`ash.rules`; a critical result produces structured damage + conditions + narrative log
line; combat log reproduces the full calculation; results survive save/load.

### M7 — Acceptance content (spec Stage 7)
Settlement, interiors, multi-level hazard area, six NPCs, 25+ movable props, branching
quest, readable clue, mechanisms, cinematic, audio, full persistence.

**Exit proof:** spec §15.1 end-to-end scenario, steps 1–12, completed from clean start
with no debug intervention — scripted as an automated playthrough where possible.

### M8 — Hardening (spec Stage 8)
Sort-cycle diagnostics, save migration, cancellation edge cases, performance, input
scaling tests, accessibility options, content validation.

### M9 — Networking (only if §3 resolves toward multiplayer)
Transport swap: `ash.sim` behind a command/event boundary, server-authoritative
validation, client prediction for local avatar movement only, drag locks, world DB
persistence, per-client presentation of cutscenes.

**Tooling (§14) runs alongside from M2, not after.** Shape-metadata editor at M2, map
editor at M2/M3, script validation at M5, runtime debug overlays continuously.
The spec is right that this content cannot be authored in code alone.

---

## 6. Sequencing rationale

M0 is independent and can run in parallel with M1–M2 by a second person. M1→M4 is a
strict chain: every one of them is a prerequisite for the next, and skipping ahead is
what produces the §17 failure modes ("a static isometric background with clickable
hotspots"). M5 must land before M7 because content authoring is otherwise
engine-coupled. M9 must be decided before M2 (it changes the object store's mutation
API) even if it is implemented last.

---

## 7. Open questions and conflicts

1. **Multiplayer vs. spec §1.3.** Must be settled first. See §3. Recommendation:
   network-clean architecture, single-player vertical slice.
2. **AT-3 data conflict.** `rules/combat_engine_rules.md` §4.3 and
   `data/combat_system_data.json` agree on AT-3 (Plate 13/1.5/22/2, Chain 11/2.0/19/2,
   Leather 9/2.5/17/2, None 7/3.5/14/2), but
   `data/attack_tables_summary.csv` disagrees (Plate 10/1.1/22/2, Chain 12/1.8/20/2,
   Leather 14/2.6/17/2). Per README's own policy this must be resolved deliberately
   and both updated. M0's loader should **throw** on cross-source disagreement rather
   than pick a winner.
3. **Rounds → real time.** The rules are turn-based; the engine is real-time
   (CMB-001..004). INTEGRATION.md §7 correctly requires one configurable conversion
   rule. Pick the value early — it changes how lethal the whole game feels.
4. **Hit-point model.** "Concussion hits" is an accumulating injury pool with no
   stated maximum in the vendored rules. Actor vitality thresholds and death rules
   need defining before M6.
5. **Asset pipeline.** The spec forbids shipping Ultima VIII assets. Original
   pixel-art shapes at 256 colours with per-frame origins and physical metadata is a
   substantial art commission — it belongs on the schedule, and the shape-metadata
   tool (M2) gates it.
6. **Content rating.** The trauma tables are graphic. Decide the target rating before
   art and text are written, especially if §2.4 (Roblox) is revisited.

---

## 8. Bottom line

Use Godot 4. It is the only mainstream option that satisfies the palette pipeline,
gives full depth-sort control, and lets the §14 authoring tools live inside the
engine — while still leaving a workable path to authoritative-server multiplayer.

Roblox trades away the parts of the spec that *are* the checkpoint in exchange for
solving the part that was never in the spec. Choose it only if reaching Roblox's
audience with low infrastructure effort matters more than the Ultima VIII-class
capability baseline — and if so, amend the spec explicitly rather than discovering
the gaps at M1.
