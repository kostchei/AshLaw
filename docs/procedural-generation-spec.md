# Procedural Generation Specification: Shadowdork Integration

This document outlines the architecture for integrating procedurally generated worlds into the `U8_Ash` engine, specifically focusing on generating 18 overarching subzones, each utilizing a dynamic interpretation of the 5-Room Dungeon pacing structure, contextualized for the Shadowdork ruleset.

## 1. Macro Structure: The 18 Subzones (1d6 Vaults)

The game world will be generated as a sequence or web of 18 distinct "subzones".

### Subzone Characteristics
- **Thematic Regions:** Each subzone represents a distinct macro-environment. Examples of these 1d6 vaults include areas of desert, forest, a castle interior, a city ward, or a seaside village.
- **Architectural Scope:** In the `Ash.Sim` layer, a subzone is a large `WorldRectangle` or a dedicated `WorldMap` that contains the generated terrain, buildings, and logic for that region.
- **Color & Atmosphere:** The generation logic will pass metadata tags (e.g., "theme_forest", "theme_desert") to the Godot presentation layer. Godot will interpret these tags to dynamically apply appropriate palettes, desaturation levels, and lighting/atmospheric effects.

## 2. Micro Structure: The 5-Room Dungeon Pacing

Within *each* of the 18 subzones, the pacing is driven by a localized, varied implementation of the 5-Room Dungeon structure. This is not a strict 5-physical-room layout, but rather a sequence of 5 paced beats or encounters.

### Room/Beat Variance
To prevent repetitive pacing, the 5 rooms will draw from a varied pool of encounter types:
1.  **Standard Encounters:** Combat or guardian scenarios.
2.  **Empty/Atmospheric:** Spaces designed purely for tension, lore, and visual storytelling, utilizing the theme's color and props.
3.  **Roleplay/Interactive:** Physics puzzles (e.g., using `prop.trestle`), lore objects, or non-hostile interactions.
4.  **Trap/Hazard:** Environmental dangers utilizing `TerrainFlags.Hazard` or pitfalls. Critically, these *must* include visual hints (e.g., specific decals, cracked floors, bloodstains) allowing an observant player to avoid them.
5.  **Safezone/Reward:** Areas containing `container.vault-box` or similar rewards (gold stacks, finesses weapons).

### Verticality
To leverage the engine's `UnitsPerLevel` (Z-axis) support system, subzones will feature vertical variance:
- **Flat:** 50% chance the 5-room sequence is primarily on one Z-level.
- **Vertical:** 50% chance the sequence includes significant elevation changes. If vertical, there is an equal chance (1/3 each) of it utilizing:
    - **Height:** Upward traversal via stairs/platforms (`PlatformZ`).
    - **Depth:** Downward traversal into pits (`PitFloorZ`).
    - **Both:** A mix of ascents and descents.

## 3. Interstitial Spaces: Rest Points & Traders

Between each of the 18 subzones, the generator will insert a guaranteed interstitial transition zone.

- **Rest Point:** Every transition zone provides a safe area for the player to rest, manage their 12-slot inventory, and prepare for the next subzone.
- **Shop/Trader (50% Chance):** In half of these transition zones, a non-hostile NPC or interaction point will spawn.
    - **Inventory:** Primarily focused on essential consumable stacks (rations, torches).
    - **Trader Types:** The trader may vary thematically (e.g., a generic merchant, a tinker offering tool repairs/upgrades, a craftsman, or a healer).

## 4. Scaling: Character Tier, Monsters, and Treasure

The procedural generation is reactive to the player's current progression.

### Character Tier
The generator reads the Avatar's `Level` and `CharacterClass` from the `ActorSheet`. This combined metric forms the "Character Tier".

### Monsters
- A `MonsterSpawner` system uses the Character Tier as a seed.
- It determines the valid pool of monster archetypes (e.g., `monster.goblin-guard` vs `monster.many-eyed-tyrant`).
- It scales the `Health`, stats, and spawned equipment (`EquipmentSlotMask`) of the selected monsters to provide an appropriate challenge based on the 1d20 resolution rules.

### Treasure
- Treasure generation utilizes the `GearSlots` pooling system.
- Basic enemies drop lower-tier loot (e.g., `item.bronze-dagger`, single coins).
- Reward rooms and bosses use `SpawnStack()` to drop high-density rewards like `GoldTypeId` or gems, ensuring the player's limited backpack capacity is challenged but not arbitrarily cluttered.
- The tier of the treasure scales directly with the Character Tier and the difficulty of the subzone.

## 5. Terrain and Building Generation implementation details

- **Chunking:** The engine will divide the subzone `WorldMap` into chunks.
- **Buildings:** Constructed by spawning objects with `ObjectFlags.Solid` over specific `TerrainCell`s.
- **Procedural Logic:** The generator will use a stochastic algorithm (e.g., Wave Function Collapse or a BSP tree) seeded by the thematic parameters to lay out the rooms, corridors, and vertical elements, validating all placement against the `PlacementResult` contract in `WorldMap`.