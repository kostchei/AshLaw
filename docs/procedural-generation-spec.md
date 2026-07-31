# Procedural generation specification

This document specifies procedurally generated worlds for the `U8_Ash` engine:
18 subzones, each paced by a varied interpretation of the 5-Room Dungeon
structure, contextualised for the Shadowdork ruleset.

## 0. Determinism

Generation is a pure function of a seed. The same seed always produces the same
world, cell for cell and object for object.

This is not a nicety. The engine already holds the line elsewhere — `Dice` keeps
an explicit xorshift state that `ObjectWorldSave` writes and `Load` resumes, so a
loaded world rolls what it would have rolled. Generation joins that discipline:

- the world seed is a `ulong`, saved with the world;
- each subzone derives its own seed from the world seed and its index, so
  subzone 7 generates identically whether or not subzones 1 to 6 were visited;
- the generator draws every choice from `Ash.Rules.Dice` — `Roll(n)`, `D6()` —
  and never from an ambient generator;
- generation produces a *plan* first (a serialisable description) and builds
  objects and terrain from it second. Planning is testable without an
  `ObjectStore`, and a plan pins what a seed means.

There are no fallbacks anywhere in generation. A plan that cannot be built — a
footprint that will not fit, a beat with no legal placement — throws rather than
quietly degrading to something simpler.

## 1. Macro structure: the 18 subzones

The world is generated as a sequence of 18 distinct subzones.

> Naming: these are **subzones**, not "vaults". In this codebase `VaultCheck` is
> the athletics roll for getting over an obstacle and `container.vault-box` is a
> chest type; reusing the word for a region would collide with both.

### Subzone characteristics

- **Thematic regions.** Each subzone is a distinct macro-environment — desert,
  forest, castle interior, city ward, seaside village.
- **Architectural scope.** A subzone is one `WorldMap`: its own terrain grid,
  extents and map id.
- **Colour and atmosphere.** The plan carries theme metadata tags (for example
  `theme_forest`, `theme_desert`) through to the Godot presentation layer, which
  interprets them as palette, desaturation and lighting choices. The simulation
  never holds a colour.

### Consequence: the engine becomes multi-map

This is the largest piece of work the specification implies, and it is worth
stating plainly rather than discovering later.

`WorldMapSet` already keys live maps by `ushort` map id, `ObjectStore` already
validates placement per map, and the save format already carries a
`CurrentMapId` and a terrain section per map. What does not yet exist:

- `PlayableSliceWorld` is single-map: `DemoMapId` is a constant and `Map` is one
  map fixed at construction. Eighteen subzones need a current-map concept and
  the ability to change it.
- Nothing transfers an actor between maps. `ObjectLocation.OnMap` names a map
  id, so the transactional machinery can express the move; no service performs
  it.
- Nothing decides which maps are resident. Eighteen fully built subzones held at
  once is the simplest correct answer and should be the first implementation;
  streaming is an optimisation to make only when it is measured to be needed.

Until that work lands, generation targets one map at a time, which is enough to
build and test every rule below.

## 2. Micro structure: the 5-beat pacing

Within *each* subzone the pacing follows a localised 5-Room Dungeon structure.
This is not five physical rooms but a sequence of five paced beats, each drawn
from a varied pool so the pacing does not read as a template:

1. **Encounter** — combat or a guardian.
2. **Atmospheric** — empty by design, for tension, lore and visual storytelling,
   carrying the theme's props and palette.
3. **Interactive** — physics puzzles (for example `prop.trestle` and its
   removable support), lore objects, non-hostile interactions.
4. **Hazard** — environmental danger via `TerrainFlags.Hazard` or a pit. These
   *must* carry a visual hint — a decal, cracked floor, bloodstain — so an
   observant player can avoid them. A hazard with no hint is a bug.
5. **Reward** — `container.vault-box` or similar: gold stacks, gems, finesse
   weapons.

### Verticality

Subzones exercise the engine's `UnitsPerLevel` Z-axis support (8 units per
Ultima VIII level):

- **Flat** — 1 in 2: the five beats sit on one Z level.
- **Vertical** — 1 in 2: the sequence includes significant elevation change, and
  then with equal probability (1 in 3 each):
  - **Height** — upward traversal via stairs and platforms, as `PlatformZ`;
  - **Depth** — downward traversal into pits, as `PitFloorZ`;
  - **Both** — a mix of ascents and descents.

Elevation change must remain traversable under the movement rules: a rise of
more than one level per step requires a `VaultCheck`, and a rise above
`VaultCheck.MaximumStepHeight` is impassable however well the player rolls. The
generator validates traversability rather than assuming it.

## 3. Interstitial spaces: rest points and traders

Between consecutive subzones the generator inserts a guaranteed interstitial
transition zone.

- **Rest point.** Every transition provides a safe area to rest, manage the
  12-slot pack, and prepare for the next subzone.
- **Trader — 1 in 2.** Half of transitions spawn a non-hostile NPC or
  interaction point.
  - **Stock:** primarily consumable stacks — rations, torches — spawned through
    the same `GearSlots` stack rules as any other goods.
  - **Types:** merchant, tinker (tool repair and upgrade), craftsman, healer.

Seventeen transitions sit between eighteen subzones. Whether a transition is its
own map or a region of the following subzone is an implementation choice; the
rest guarantee and the 1-in-2 trader chance are not.

## 4. Scaling: character tier, monsters, treasure

Generation is reactive to player progression.

### Character tier

The generator reads the Avatar's `Level` and `Class` from its `ActorSheet`.
Together these form the *character tier*, which is an input to generation, not a
second progression system.

### Monsters

- A monster spawner takes the character tier and the subzone's seed.
- It selects a pool of archetypes for that tier — `monster.cave-rat` at the low
  end, `monster.many-eyed-tyrant` at the high.
- It scales `MaxHealth`, ability scores and spawned equipment
  (`EquipmentSlotMask`) so encounters stay meaningful under the d20 resolution
  rules in `AttackResolver`.

### Treasure

- Treasure spawns through the `GearSlots` pooling rules, never around them.
- Ordinary enemies drop low-tier loot — `item.bronze-dagger`, single coins.
- Reward beats and bosses use stack spawning for dense rewards — gold, gems — so
  the pack's 12-slot limit is challenged rather than cluttered.
- Treasure tier scales with character tier and subzone difficulty.

## 5. Terrain and building generation

- **Chunking.** A subzone's `WorldMap` is generated in chunks.
- **Buildings.** Constructed by spawning objects carrying `ObjectFlags.Solid`
  over the relevant `TerrainCell`s, and by setting solid terrain where a wall is
  scenery rather than an object.
- **Layout algorithm.** A stochastic layout — BSP subdivision, or wave function
  collapse — seeded from the subzone seed and constrained by the theme, lays out
  rooms, corridors and vertical elements.
- **Validation.** Every placement goes through `WorldMap.ValidatePlacement`, the
  single physical placement contract. The generator does not write object
  positions directly, and a rejected placement is an error in the plan, not a
  cell to skip.
