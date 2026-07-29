# Extra Class Rules

This branch adds eight playable classes to the original Fighter, Priest
(`Cleric` in code), Thief (`Rogue` in code), and Wizard.

All twelve classes use the same advancement grammar:

1. Each level from 1 through 20 is either a **pinned class ability** or an open
   **2d6 class-talent roll**.
2. Martial and hybrid talent tables put their attribute boost on **7-9**
   (15/36, or 41.7%).
3. Full pact and spellcasting talent tables put their attribute boost on
   **6-9** (18/36, or 50%).
4. A roll of **2 or 12** is always the class wildcard: +2 to any attribute or
   any other option from that class table.
5. Attack progression reuses one of the four established diminishing curves.
   Class identity comes from pinned features and talent outcomes, not a new
   combat-math scale.

The executable definitions are in
`src/Ash.Rules/ClassAdvancement.cs`; the level 1-30 attack values are in
`vendor/ash-v1-rules/data/class_progression.csv`.

## Curve and talent calibration

| Class | Design basis | Attack curve | Open 2d6 levels | Attribute band | Expected boosts |
|---|---|---:|---:|---:|---:|
| Bard | AD&D 1e fighter/thief/druid bard | Priest (+15) | 9 | 6-9 | 4.50 |
| Barbarian | AD&D 1e *Unearthed Arcana* barbarian | Fighter (+20) | 9 | 7-9 | 3.75 |
| Sorcerer | Charisma defiler in `docs/sorcerer.md` | Wizard (+6) | 8 | 6-9 | 4.00 |
| Paladin | AD&D 1e paladin plus Pendragon chivalry | Fighter (+20) | 8 | 7-9 | 3.33 |
| Celestial Warlock | Celestial pact caster | Wizard (+6) | 9 | 6-9 | 4.50 |
| Monk | AD&D 1e monk | Priest (+15) | 8 | 7-9 | 3.33 |
| Ranger | AD&D 1e ranger | Fighter (+20) | 9 | 7-9 | 3.75 |
| Assassin | AD&D 1e assassin | Thief (+10) | 9 | 7-9 | 3.75 |

## Pinned identity

- **Bard:** martial and thief inheritance, charm by music, inspiration, legend
  lore, colleges, languages, and druidic spellcasting.
- **Barbarian:** exceptional toughness, fast movement, wilderness skills, back
  protection, detection of magic and illusion, a war-band, and spellbreaking.
- **Sorcerer:** Charisma casting, life-defiling spellfire, spell absorption,
  Flesh/Solid/Gas/Fluid/Soul Destruction, metamagic, counter-magic, phantoms,
  physical-rule bypass, and node mastery.
- **Paladin:** detect malign forces, protection, lay on hands, divine health,
  warhorse, turning, late priest spells, holy sword, and a chivalric code.
- **Celestial Warlock:** pact magic, healing light, radiant spellfire,
  invocations, sanctuary, celestial resilience, and searing vengeance.
- **Monk:** scaling open-hand combat, speed, deflection, bodily purity,
  enchanted hands, quivering palm, magic resistance, and ethereal mastery.
- **Ranger:** tracking, wilderness surprise, giant-slayer training, dual
  druid/magic-user spells, animal companion, and exceptional followers.
- **Assassin:** delayed thief skills, studied assassination, disguise, poison,
  secret languages, and guild mastery.

## Paladin chivalry

Paladins track Pendragon's six chivalric virtues on the same 1-20 scale already
used by priest virtues:

**Energetic, Generous, Just, Merciful, Modest, and Valorous.**

Their combined score must be at least **80**. Falling below 80 suppresses the
paladin's supernatural class abilities until atonement; ordinary martial
training remains. `PaladinChivalricCode.Check` is the runtime check.

## Adaptation boundary

“As 1e” means that the recognizable class identity and milestone order are
preserved within Ash's level-20, 2d6-talent framework. These are adaptations,
not verbatim reproductions of historical rules tables. The celestial warlock
and defiler sorcerer are authored Ash classes.
