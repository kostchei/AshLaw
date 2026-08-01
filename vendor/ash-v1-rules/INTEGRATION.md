# U8_Ash Rules Integration

This document defines the initial boundary between the vendored Ash V1 rules and
the U8_Ash world, actor, combat, scripting, and UI systems.

It is an integration map, not a rewrite of the rules. The source rules remain
authoritative for formulas, progressions, thresholds, and trauma text.

## 1. Responsibility split

| Rules package owns | U8_Ash engine owns |
|---|---|
| Class attack-modifier progression | Actor identity, position, animation, inventory, and persistence |
| Attack categories and armor parameters | Collision, range, line of sight, facing, and target validity |
| Net-roll and hit calculations | Input, targeting, attack timing, and process scheduling |
| Critical severity and trauma selection | Applying damage, conditions, movement, effects, and death |
| Spellcasting restrictions and mishaps | Spell objects, projectiles, areas, camera, audio, and UI |
| Narrative trauma outcomes | Converting trauma directives into world and actor state changes |

The engine must not duplicate the combat equations in UI, AI, scripts, and
network-independent presentation code. One rules service should resolve all
attacks.

## 2. Core resolution contract

The combat rules use:

```text
Net Roll = raw d20 + Attack Modifier - Defense Modifier
```

The selected attack category and target armor determine:

- hit threshold;
- damage-curve origin;
- concussion-damage multiplier;
- quadratic damage coefficient (zero for a linear curve);
- base critical threshold;
- critical interval;
- associated critical table.

If the attack hits, the rules service returns both numerical and semantic
results. The engine then applies those results through authoritative world and
actor services.

## 3. Proposed input model

```text
AttackRequest
  attacker_id
  target_id or target_position
  raw_d20

  attack_category_id
  attack_modifier
  defense_modifier
  armor_type

  weapon_id or spell_id
  damage_type
  size_category

  range
  facing
  situational_modifiers[]
  rules_flags[]
```

The engine is responsible for validating that the target, range, facing,
weapon, ammunition, spell cost, and action timing are legal before requesting
resolution.

## 4. Proposed output model

```text
AttackResult
  hit
  raw_d20
  net_roll
  margin

  concussion_hits
  critical_tier
  critical_table_id
  trauma_index
  trauma_text

  activity_penalty
  stun_rounds
  bleed_rate

  conditions[]
  forced_movement
  item_effects[]
  target_effects[]

  mishap
  messages[]
```

Narrative trauma text must be retained for combat logs and player feedback, but
world changes should use structured fields rather than reparsing prose at
runtime.

## 5. Runtime data mapping

### 5.1 Attack categories

`data/combat_system_data.json` defines:

- `at_1_1_handed_slashing`;
- `at_2_1_handed_concussion`;
- `at_3_2_handed_weapons`;
- `at_4_missile_weapons`;
- `at_5_tooth_and_claw`;
- `at_6_grappling_unbalancing`;
- `at_7_spell_bolts`;
- `at_8_spell_balls`;
- `environmental_fall_crush`.

Weapons, creature attacks, spells, and hazards should reference one of these
stable category IDs rather than copying its values into each item definition.

### 5.2 Physical critical tables

Each physical CSV provides one shared 1–18 outcome vector:

- `ct_1_crush_critical_table.csv`;
- `ct_2_slash_critical_table.csv`;
- `ct_3_puncture_critical_table.csv`;
- `ct_4_unbalancing_critical_table.csv`.

The runtime converts the raw d20 to a 1–10 sub-index and adds the critical
tier's modifier (`A +0`, `B +2`, `C +4`, `D +6`, or `E +8`). Indices 1–4 are
padding, and index 5 (MERP band 01–05) is also a no-effect result. Structured
effects begin at index 6. MERP stunned results are represented by Restrained;
Prone, Incapacitated, Dying, Exhaustion, Injured, stable-at-zero recovery, and
mastery effects appear only when the source
outcome supplies an equivalent mechanic.

### 5.3 Spellcasting rules

The JSON contains configuration for:

- attacks of opportunity;
- armor restrictions;
- Cleric deity alignment;
- natural-1 spell mishaps.

These rules should be evaluated by the shared rules service and surfaced to the
world through normal action, reaction, and process systems.

## 6. Actor-stat mapping

The first integration pass needs these actor-facing concepts:

| Rules concept | Engine-facing field or service |
|---|---|
| Attack Modifier / OB | Derived actor attack value for a weapon or spell category |
| Defense Modifier / DB | Derived defense value including agility, armor, shield, and parry |
| Strength bonus | Attribute modifier used for armor penalty compensation and other rules |
| Dexterity bonus | Attribute modifier contributing to defense |
| Armor category | Derived from equipped armor objects |
| Shield modifier | Derived from equipped shield and current action state |
| Parry modifier | Allocated or derived defensive action modifier |
| Concussion hits | Applied to actor injury or vitality state |
| Activity penalty | Applied to actor checks and attack/defense derivation |
| Conditions | Applied through the shared actor condition system |

Equipment remains persistent world objects. The rules service receives derived
values; it does not own the inventory or equipment graph.

## 7. Condition mapping

The rules refer to conditions such as:

- prone;
- restrained;
- incapacitated;
- stunned;
- unconscious;
- bleeding;
- activity penalties;
- forced movement;
- limb or equipment injury;
- death.

Each condition needs a structured engine representation with:

```text
Condition
  type
  source_id
  magnitude
  duration
  stacking_policy
  removal_policy
  presentation_key
```

Rounds in the tabletop rules must map to a defined duration in the real-time
engine. The conversion should be a single configurable rule, not embedded in
individual trauma outcomes.

## 8. Attack process

An engine attack should proceed as follows:

1. Validate actor state, weapon or spell, target, range, and resource cost.
2. Start the attack or casting animation process.
3. At the configured action frame, construct `AttackRequest`.
4. Resolve it through the shared rules service.
5. Apply concussion hits and structured trauma effects atomically.
6. Start resulting condition, forced-movement, projectile-impact, death, or
   equipment-damage processes.
7. Emit combat-log, bark, animation, particle, and audio presentation from the
   committed result.
8. Persist all consequential state.

AI and player attacks must use the same resolution path.

## 9. UI requirements

The UI should expose:

- attack category and weapon;
- current Attack Modifier;
- target Defense Modifier only when game design permits;
- d20 roll and net roll;
- hit or miss;
- concussion hits;
- critical tier and trauma text;
- applied conditions and durations;
- spell mishaps and provoked reactions.

Compact feedback may summarize the result, while a combat log retains the full
resolution trace for testing and player inspection.

## 10. Initial implementation order

1. Load and validate `combat_system_data.json`.
2. Define stable enums or identifiers for attack category, armor, critical
   table, critical tier, and condition.
3. Implement a pure deterministic attack-resolution function.
4. Import the four physical critical CSVs.
5. Convert trauma results into structured effect data.
6. Add actor-derived Attack Modifier and Defense Modifier services.
7. Connect one melee weapon and one armor type end to end.
8. Add critical effects and conditions.
9. Add missile and creature attacks.
10. Add spell bolts, spell balls, restrictions, provocation, and mishaps.
11. Add hazards and falling.
12. Build deterministic fixtures from the worked examples in the rulebooks.

## 11. Integration acceptance

The first rules integration is complete when:

- the same request always produces the same result;
- a worked rulebook example passes as an automated fixture;
- player and AI attacks use one resolver;
- equipment-derived armor and modifiers come from persistent objects;
- a critical result produces structured damage, conditions, and narrative text;
- results survive save/load;
- the combat log can reproduce the full calculation;
- changing a JSON or CSV value changes resolution without engine-code changes.
