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

`WorldMapSet` keys live maps by `ushort` map id, `ObjectStore` validates
placement per map, and the save format carries a `CurrentMapId` and a terrain
section per map. On top of that:

- `PlayableSliceWorld` is multi-map. `CurrentMapId` is derived from the map the
  Avatar is standing on rather than stored, so a transition and a load both
  change it by moving him and nothing else has to be kept in step.
  The full planner can still describe all eighteen subzones. The current
  vertical slice has `PlayableSliceWorld.CreateGenerated` build only index zero
  and start the confirmed character at its way in; its exit is the completion
  boundary rather than a transition to an eagerly built second map. `DemoMapId`
  remains only the hand-built
  acceptance map, which keeps the physics area no generated subzone carries.
- `MapTransitionService` performs the move, and `SubzoneBuilder` gives each
  subzone the two ends it needs: a `portal.entrance` on every subzone after the
  first and a `portal.exit` on every one before the last. Both ends are objects
  in the world, so a loaded world can be left the same way a freshly generated
  one can — nothing has to reconstruct the plan that made them.
- The one-subzone milestone keeps exactly one generated map resident. Expanding
  back to the planned eighteen maps follows only after character creation,
  treasure, advancement and the five-beat pacing are balanced.

## 2. Micro structure: the 5-beat pacing

Within *each* subzone the pacing follows a localised 5-Room Dungeon structure.
This is not five physical rooms but a sequence of five paced beats, each drawn
from a varied pool so the pacing does not read as a template:

The runtime does not count, discover, activate, or complete these beats. They
become ordinary terrain, objects and creatures. Their consequences arise from
the same contextual interaction, hazard, physics, perception and combat rules
as the rest of the world, and separate encounters may overlap spatially.

1. **Encounter** — a static monster which can be fought, evaded or lured.
2. **Atmospheric** — empty by design, for tension, lore and visual storytelling,
   carrying the theme's props and palette.
3. **Interactive** — physics puzzles (for example `prop.trestle` and its
   removable support), lore objects, non-hostile interactions.
4. **Hazard** — environmental danger via `TerrainFlags.Hazard` or a pit. The
   first cracked-floor implementation makes a Dexterity check on entry and
   deals 1d4 concussion damage on failure. Hazards *must* carry a visual hint —
   a decal, cracked floor, bloodstain — so an observant player can avoid them.
   A hazard with no hint is a bug.
5. **Reward** — `container.vault-box`, its legendary relic, and an independently
   acting guard which need not be killed.

Two of the five paced spaces therefore contain a static threat (40%), matching
the small-delve density target without packing every space with combat. A later
wandering-monster slice may check every three rounds; it must spawn ordinary
creatures into this same perception/territory system, not create room state.

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
- Level one selects two distinct entries from the ten-profile pool: five
  Shadowdark adaptations and five stable outputs from the seeded system
  generator. Higher tiers currently retain the guard/tyrant scaling bridge.
- `MonsterProfile` supplies concussion, abilities, armour category, exact
  attack/defence bonuses, attack table, movement, special/weakness hooks, voice
  and carried loot. Its canonical fingerprint protects saves from reinterpretation.
- Killing monsters awards no XP. Monsters are obstacles controlling terrain or
  treasure, so evasion, bribery, luring and traps remain valid solutions.

### Treasure

- Treasure spawns through the `GearSlots` pooling rules, never around them.
- Ordinary enemies carry low-tier loot which remains on their corpse; no room
  or encounter-completion state is required to create the drop.
- Reward beats and bosses use stack spawning for dense rewards — gold, gems — so
  the pack's 12-slot limit is challenged rather than cluttered.
- Treasure tier scales with character tier and subzone difficulty.
- The first vault's Ash Crown Shard is a legendary hoard worth 10 XP, the flat
  threshold for level two. Carousing and normal/extraordinary hoard awards are
  later progression content.

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
