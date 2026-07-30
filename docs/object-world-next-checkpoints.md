# Object world: next checkpoints

This document turns the next object-world work into implementable checkpoints.
It starts from the systems already in `main`:

- one authoritative generational `ObjectStore`;
- exactly one `ObjectLocation` per live object;
- transactional movement between map, container, backpack, and equipment;
- integer world coordinates: 256 units per tile and 8 units per vertical level;
- immutable `WorldObject` snapshots consumed by the renderer and UI.

The objective is a persistent physical world in which objects collide, rest on
terrain or other objects, fall when support disappears, and reload without
changing identity or state.

**Implementation status (2026-07-30):** checkpoints 1, 2 and 3 are complete.
`WorldMap` owns terrain and a deterministic footprint-bucket/anchor index; store
commits rebuild one synchronous revision. `WorldMap.ValidatePlacement` is the one
physical placement contract, `ObjectTransferService` runs it as a validation
stage of the same transaction, and `MovementSolver` resolves swept integer
movement against solid objects, solid terrain and map bounds.

Checkpoint 3 added `SupportRef`, `MotionState`, vertical velocity and step
height to the store, support resolution and support back-links to `WorldMap`,
and `PhysicsSystem` for support maintenance, gravity, landings, carried
dependants and the physical invariants. `ObjectTransferService.Execute` now
commits locations and physics fields in one transaction.

Checkpoint 4, object-world Save v1, is in progress. Landed so far:

- the canonical format (`ASHW`, format and minimum-reader versions, content
  fingerprint, tick, current map, payload length and SHA-256) in
  `ObjectWorldSave`, with an atomic temp-file write that reads the file back
  before it replaces the destination;
- exact object-store fidelity through `ObjectStore.Capture` and
  `ObjectStore.Restore`: every slot including dead ones, their generations, and
  the free-slot stack order, so handles are never remapped, a stale handle stays
  stale, and allocation after a load reuses the same slots in the same order;
- derived caches stay out of the payload — spatial buckets, support back-links,
  visible-object lists and sort order are rebuilt on load, while the
  authoritative support *references* are saved;
- the terrain section (format version 2): every map's identity, extents and
  terrain cells. **Save v1 stores the whole grid, not deltas.** The design in
  1.1 is authored map content plus a runtime-delta layer, but no authored
  content exists — the demo map is still built in code — and a save that depends
  on content it cannot name is worse than one that is self-contained. When
  authored maps arrive, this section becomes a base-map id plus deltas and the
  format version bumps again;
- loading builds a complete world beside the live one — store first, then its
  maps, then index and physics invariants — and **never re-settles**. A world
  resumes on the tick it was saved on with mid-fall objects still falling;
  settling belongs only to starting a world from nothing. `PlayableSliceWorld`
  gains `Save`/`Load` on exactly those terms.

Still open in checkpoint 4:

- the save boundary in 4.1: saving only at the end of a completed tick, with no
  transfer in flight and a deferral when a drag is `InTransfer`;
- adopting a loaded world into a running session: `ObjectWorldSave.Restore`
  already builds the replacement without touching the live world, so what
  remains is the swap itself and the in-game save/load commands.

Conventions settled while implementing checkpoints 2 and 3:

- a footprint is anchored at the far corner of the cell it occupies, so the
  anchor of cell `c` is `(c + 1) * 256` and every placed footprint lies inside
  the map's world bounds;
- solid terrain blocks every object, while object-versus-object collision is
  solid-against-solid, so ground items and loot still share a cell with an actor;
- nothing may sit below the floor of a cell it covers, which is what makes a
  raised platform an obstacle instead of somewhere to embed;
- every `Solid` volume supports what lands on it, and `ProvidesSupport` adds the
  same top face to volumes that are not solid, such as a bridge deck. Nothing
  that can stop a fall is ever missing a support surface;
- the support rule is the anchor point rule from 3.1: the supported object's
  anchor corner unit must lie on the supporting face;
- a step up is resolved before the horizontal sweep, so the sweep runs at the
  elevation the move ends on. The vertical transition itself is not swept in v1.

`ObjectStore.Create` remains outside placement validation: spawning builds the
initial world before its maps exist. Placement is enforced on every transfer.
Anything gravity-affected spawns `Falling`, so the first physics tick — not the
spawn — decides where it rests.

Deferred from checkpoint 3: fall damage stays out until the environmental
fall/crush table has an accepted source, as 3.4 requires. Distance and landing
speed are already recorded on the landing event.

## Delivery order

| Checkpoint | Result |
|---|---|
| 1. Map container and spatial index — done | Authoritative map ownership and shared spatial queries |
| 2. Collision and placement — done | Solid volumes block movement and invalid placement cannot commit |
| 3. Elevation, support, and gravity — done | Objects stack, retain support, fall, and land deterministically |
| 4. Object-world Save v1 — in progress | The complete object world round-trips byte-identically |
| 5. Direct manipulation | Mouse selection and drag/drop use the same transfer and placement contract |
| 6. Quantities and inventory rules | Stack merge, split, partial transfer, weight, and content restrictions |

Checkpoints 1–4 are the immediate object-world milestone. Direct manipulation
and quantities follow after physical placement and persistence are trustworthy.

## 1. Map container and spatial index

### 1.1 Authoritative map state

Introduce `WorldMap` in `Ash.Sim`. A map owns:

- a stable `ushort MapId`;
- integer horizontal bounds;
- terrain cells and their floor elevation;
- terrain collision/support flags;
- the derived spatial index for objects whose location is `OnMap(MapId, ...)`;
- a monotonically increasing world revision.

Terrain is not represented as thousands of ordinary object handles. Terrain
cells are immutable map content plus an explicit runtime-delta layer for changes
such as a collapsed floor or raised platform. Props, doors, platforms, actors,
items, and triggers remain ordinary objects.

`ObjectStore` remains the owner of object identity and components. `WorldMap`
does not duplicate positions or object state.

### 1.2 Uniform spatial index

Index every `OnMap` object into all uniform-grid buckets touched by its horizontal
footprint. Provide deterministic queries for:

- point and tile;
- horizontal rectangle;
- three-dimensional volume;
- swept movement volume;
- region;
- capability flags such as `Solid`, `Actor`, `Item`, or `Container`.

Query results are unique and ordered by `ObjectId`, regardless of bucket layout.
Contained, equipped, transferred, destroyed, or differently mapped objects never
appear in the result.

The first implementation is a correctness structure, not a synthetic performance
project. Optimisation follows real scene evidence.

### 1.3 Atomic commit integration

Extend the existing transaction boundary rather than creating another object
mutator:

1. validate the projected final object-location graph;
2. validate physical placement for every final `OnMap` destination;
3. build the affected spatial buckets from the projected state;
4. commit object locations and the new index revision together;
5. publish location/index change events after the commit.

A rejected transfer leaves both the object store and spatial index unchanged.
Opening a door, transforming a monster, destroying an object, or changing a
physical capability must invalidate the same authoritative index.

### 1.4 Migration

Replace full-store scans in:

- player movement and collision;
- nearest chest/corpse selection;
- melee targeting;
- ground pickup;
- visible-object collection;
- future AI and pathfinding.

### 1.5 Exit proof

- Place 1,000 objects across several regions and maps.
- Move objects through map, backpack, chest, and equipment.
- Verify every query after each successful commit.
- Inject failed transfers and confirm the index revision and contents do not
  change.
- Destroy and reuse object slots without leaving ghost index entries.
- Preserve deterministic query order across repeated runs.

## 2. Collision and placement

### 2.1 One volume convention

Add a single `BoundsFor(WorldObject)` function producing an integer world volume:

```text
[x_min, x_max) × [y_min, y_max) × [z_min, z_max)
```

Half-open bounds mean touching faces are not penetration. Footprint anchoring is
defined once in this function using the authored footprint and object position;
rendering, collision, support, and hit testing must not independently reinterpret
the anchor.

All intermediate multiplication and swept-volume comparison uses checked
64-bit integers. Simulation code does not use floating point.

### 2.2 Physical components

Extend the store with explicit physical data:

- collision footprint and height;
- `Solid`;
- `ProvidesSupport`;
- `AffectedByGravity`;
- `Fixed` or movable;
- maximum step-up/down where applicable;
- motion state and vertical velocity;
- current support reference.

Presentation flags do not determine physics. A shape change may update physical
components only through an authoritative state change that revalidates placement.

### 2.3 Collision queries

The movement solver must:

1. build the swept volume from the current to proposed position;
2. query possible blockers through `WorldMap`;
3. reject objects that do not overlap vertically or are not solid;
4. calculate the first blocking contact deterministically;
5. identify the specific blocking object or terrain cell;
6. return a resolved position or a typed failure without mutating the world.

Ties use a stable key ending in `ObjectId`. Actors, monsters, and movable props
use the same collision service. The initial playable movement can remain
cardinal and grid-stepped, but the solver contract must accept integer
displacements so later walk/run motion does not require replacement.

### 2.4 Placement

An object transferred onto a map must pass:

- map bounds;
- collision/overlap;
- valid support or an explicit falling placement;
- object movability and reach policies;
- map/content restrictions.

Physical placement becomes a validation stage inside the existing object
transaction. It does not move an object first and repair it afterward. Failed
placement retains the exact source location and identity.

### 2.5 Exit proof

- Solid objects and terrain block both actors and movable props.
- The failure identifies the exact blocker.
- Opening or transforming an object changes collision at the same commit.
- No successful movement leaves overlapping solid volumes.
- Failed placement leaves object state, index revision, and support graph
  unchanged.

## 3. Elevation, support, and gravity

### 3.1 Terrain and object surfaces

Every terrain cell exposes a floor elevation and support capability. An object
with `ProvidesSupport` exposes its top face at `z_min + height`.

Support candidates come from the spatial index beneath the object's footprint.
Choose the highest valid surface at or below the proposed base elevation. Equal
heights resolve deterministically by:

1. terrain before object support;
2. terrain cell coordinate or lowest `ObjectId`.

The first version uses a documented support-footprint rule shared by placement
and gravity. A practical v1 rule is that the supported object's footprint anchor
must lie within the supporting top face. More elaborate balance and multi-point
support can be added later without changing the `SupportRef` contract.

### 3.2 Support references and back-links

Use a tagged support reference:

```text
None
Terrain(map, cell, top_z)
Object(object_id, top_z)
```

Each grounded actor or gravity-affected object has one current support.
`WorldMap` derives sorted back-links from a supporting object to the objects
resting on it. Back-links are a cache and are rebuilt from authoritative support
references.

Removing, moving, destroying, or disabling a support invalidates its dependants
in `ObjectId` order. They either resolve another support immediately or enter
`Falling`.

### 3.3 Elevation-aware movement

A horizontal move resolves in this order:

1. sweep against solid volumes;
2. find the highest support at the destination;
3. reject a rise above the actor's allowed step height;
4. snap to an allowed support elevation;
5. detach from old support and attach to the new support in the same commit.

This supports stairs, low platforms, bridges, tables, stacked crates, and later
moving platforms without a separate collision grid.

### 3.4 Gravity

Gravity runs on the fixed simulation tick:

- velocity is integer world units per tick with an integer remainder if needed;
- unsupported affected objects transition to `Falling`;
- vertical movement uses a swept query so fast objects cannot pass through a
  thin support;
- landing selects the highest support crossed during the tick;
- teleportation, destruction, containment, or scripted relocation cancels the
  old fall safely;
- landing emits a world event after the state commit.

Moving supports carry their supported objects using one projected physics commit.
If carrying would embed a dependent object, the policy must be explicit: block
the support, detach the dependent, or invoke a content rule. V1 should block the
support and report the blocker.

Fall distance and landing speed should be recorded for future rules. Actual fall
damage remains deferred until the currently unvalidated environmental fall/crush
table has an accepted source. Gravity, falling, landing, and hazard events do not
need to wait for damage rules.

### 3.5 Physical invariants

After every physics commit:

- every grounded object references a live valid support;
- every support back-link agrees with the supported object's reference;
- no solid object is embedded in another solid volume;
- contained/equipped objects have neither map collision nor map support;
- a falling object has no support and a valid map location;
- a fixed object is never advanced by gravity;
- index bounds agree with authoritative object bounds.

### 3.6 Playable acceptance area

Expand the test map with:

- stairs to a raised stone platform;
- a table that supports movable loot;
- stackable crates at several elevations;
- a bridge or moving platform test;
- an object whose support can be removed;
- a lower floor on which falling objects land.

The F3 overlay should add collision volume, base/top elevation, motion state, and
support identity.

### 3.7 Exit proof

- The Avatar crosses several elevations using normal movement.
- Loot placed on furniture remains at the correct top elevation.
- Removing support causes every affected object to fall.
- Falling always stops on the highest crossed valid support.
- Repeating the same tick/input sequence produces identical positions, support
  identities, and events.
- Headless tests cover support removal, stacked objects, interrupted falling,
  moving support, and collision during carrying.

## 4. Object-world Save v1

### 4.1 Save boundary

Save only at the end of a completed simulation tick:

1. finish the current object/physics transaction;
2. verify no half-completed transfer is being committed;
3. defer the save if a future drag transaction is in `InTransfer`;
4. validate world invariants;
5. take one immutable world snapshot.

The save operation never observes a partially updated object store, spatial
index, or support graph. UI should report when a save is deferred to the next safe
tick.

### 4.2 Canonical format

Use a versioned, canonical binary format for Save v1:

```text
magic "ASHW"
format version and minimum reader version
game/content fingerprint
simulation tick and current map
payload length and SHA-256
canonical world payload
```

The payload contains integers and length-prefixed UTF-8 identifiers; no
floating-point state, platform-native sizes, timestamps, or unordered
dictionary output. V1 is uncompressed so byte identity is easy to inspect and
test. Compression can wrap a later format without changing the canonical
payload.

Write to a sibling temporary file, flush it, verify its checksum, then replace
the destination atomically. A crash must leave either the prior valid save or the
new valid save.

### 4.3 Serialized authoritative state

Save:

- every object-store slot, including its generation and live/dead state;
- free-slot stack order so future allocation remains deterministic;
- every live object's components and exact `ObjectLocation`;
- health, quantity, condition, quality, container state, and equipment masks;
- map identifiers and persistent terrain/runtime deltas;
- motion state, vertical velocity/remainder, and authoritative support reference;
- current simulation tick and other object-world sequence counters;
- content fingerprint required to interpret type and shape identifiers.

Object IDs are restored exactly, not remapped. A stale handle before saving must
remain stale after loading.

Do not save derived caches:

- spatial buckets;
- support back-links;
- visible-object lists;
- render sort order;
- pathfinding data;
- Godot nodes or textures;
- nonessential open UI panels.

### 4.4 Staged load

Loading never mutates the live world incrementally:

1. read and validate header, version, size limits, checksum, and content
   fingerprint;
2. deserialize into a staging `ObjectStore` and map set;
3. resolve every parent, equipment, map, and support reference;
4. run object, collision, and support invariants;
5. rebuild spatial indices and derived support back-links;
6. rebuild render/visibility caches;
7. atomically replace the live world only after every check passes.

An incompatible or corrupt save fails with a clear error and leaves the current
world untouched. The loader must reject duplicate handles, invalid generations,
parent cycles, impossible locations, out-of-bounds positions, and invalid
component ranges.

### 4.5 Migration policy

- Writers emit only the current schema.
- Readers dispatch by explicit format version.
- Compatible old versions migrate through pure, tested transforms.
- The original bytes remain available if migration fails.
- A content fingerprint mismatch is an explicit compatibility error unless a
  declared migration handles it.

### 4.6 Exit proof

- A world containing at least 1,000 objects round-trips with all invariants.
- `save → load → save` produces byte-identical files.
- Map, backpack, chest, corpse, and equipment locations retain exact handles.
- Destroyed generations and future slot-reuse order survive loading.
- Grounded, supported, and mid-fall objects resume deterministically.
- Spatial queries before save and after load return identical ordered handles.
- Corrupt length, checksum, parent, support, and version fixtures fail without
  changing the live world.
- Repeated save/load cycles do not duplicate or orphan objects.

## 5. Subsequent checkpoints

After Save v1:

1. sprite-mask world selection and one drag state machine;
2. world/container/equipment drop targets using transactional physical placement;
3. cancellation and reach validation;
4. stack compatibility, merge, split, and partial transfer;
5. weight, volume, and container content restrictions;
6. derived statistics from authoritative equipped objects.

These features should not introduce a second inventory, equipment, collision, or
save model. They remain consumers of the object store, `WorldMap`, transfer
service, placement solver, and Save v1 snapshot.
