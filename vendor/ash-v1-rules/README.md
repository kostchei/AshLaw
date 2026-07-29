# Ash V1 Rules

This directory vendors the first-edition D&D-like tabletop rules developed in
`D:\Code\ash_v1` for use by U8_Ash.

The rules blend AD&D 1st/2nd Edition class archetypes with Rolemaster/MERP-style
attack categories, diminishing progression brackets, and detailed critical
trauma. They use a single d20 roll for hit resolution, concussion damage,
critical severity, and the trauma-table index.

## Start here

Read the package in this order:

1. [Class progression tables](rules/class_progression_tables.md)
   - Four attack-modifier curve archetypes shared by twelve playable classes.
   - Level brackets, caps, spellcasting constraints, and class talent tables.

2. [Combat engine rules](rules/combat_engine_rules.md)
   - Net-roll equation.
   - Defense and armor interaction.
   - Hit and critical calculations.
   - Eight attack-table categories.
   - Environmental impact and spellcasting rules.

3. [Critical-hit system](rules/critical_hits_system.md)
   - Critical severity tiers A-E.
   - Single-roll trauma indexing.
   - Crush, slash, puncture, and unbalancing outcomes.
   - Hazards, creature attacks, and elemental criticals.

4. [Combat system data](data/combat_system_data.json)
   - Runtime-oriented configuration for attack categories and spellcasting
     constraints.

5. CSV tables in [`data/`](data/)
   - Compact attack-table summary.
   - Twelve-class attack-progression table.
   - Four physical critical-trauma tables.

## Package layout

```text
ash-v1-rules/
  README.md
  INTEGRATION.md
  PROVENANCE.md
  rules/
    class_progression_tables.md
    combat_engine_rules.md
    critical_hits_system.md
  data/
    combat_system_data.json
    attack_tables_summary.csv
    class_progression.csv
    ct_1_crush_critical_table.csv
    ct_2_slash_critical_table.csv
    ct_3_puncture_critical_table.csv
    ct_4_unbalancing_critical_table.csv
```

## Canonical and derived material

- The Markdown files are the human-readable design authority.
- `combat_system_data.json` is the primary runtime-oriented configuration
  currently present in the source package.
- The CSV files are compact import and lookup tables.
- If data and prose disagree, resolve the disagreement deliberately and update
  both rather than silently selecting one.

## U8_Ash relationship

These rules provide the numerical and narrative resolution layer for actors,
weapons, armor, attacks, spells, hazards, conditions, and advancement. The
[world, interaction, and UI specification](../../docs/ultima-viii-class-world-interaction-ui-spec.md)
defines how those results inhabit and persist in the Ultima VIII-class object
world.

See [INTEGRATION.md](INTEGRATION.md) for the proposed boundary between the rules
package and the game engine.

## Vendoring policy

The rules and data are copied from the source package rather than rewritten.
Only absolute local-file links were changed to portable relative links.

Operational files used to upload or synchronize the source documents are not
part of the rules package and were intentionally excluded. See
[PROVENANCE.md](PROVENANCE.md).
