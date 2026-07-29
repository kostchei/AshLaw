# ADR 0004: Collapse MERP's two leather columns into one runtime armour type

- **Status:** Accepted, revisit before M6
- **Date:** 2026-07-29

## Context

The published MERP attack tables have **five** armour columns: Plate, Chain,
Leather-Rigid, Leather-Soft, and None. The engine's `ArmorType` has **four**: Plate,
Chain, Leather, None.

Something has to happen to the fifth column. The two leather columns are not close
together on every table — at the final row they differ by 3 hits on AT-1 (22 vs 25)
and by 4 on AT-5 (32 vs 36) — so the choice is not cosmetic.

This ADR was referenced by `tools/import/merp_reference.json` before it was written.

## Decision

Collapse Leather-Rigid and Leather-Soft into a single `Leather` column using the
**arithmetic mean** of the two source values.

The two source columns are retained separately in
`docs/reference/merp-attack-tables.csv` and in `merp_reference.json` so the collapse
can be revisited without re-reading the books. The curve fit and the final-row
validator both check the fitted `Leather` column against that mean, not against
either source column alone.

The alternatives were:

- **Pick rigid, discard soft** (or the reverse). Rejected: it silently biases every
  leather matchup toward one end of the range, and there is no principled reason to
  prefer either.
- **Restore the fifth column now.** Rejected *for now* rather than on the merits — it
  is the better model, but it is only worth paying for once equipment data exists to
  distinguish the two, which is M6.

## Consequences

- Leather results sit midway between the two published columns and match neither
  exactly. No leather matchup is faithful to source at the 1-hit level.
- Armour stays a four-value enum, which keeps `ArmorType`, the JSON `armor_targets`
  objects, and `attack_tables_summary.csv` aligned at four rows per table.
- The decision is reversible cheaply, because nothing has been discarded: both source
  columns are still in the repository.
- **This must be settled before equipment data is authored** (build plan §5.1c). If
  leather is meant to be a progression — hide, soft leather, rigid leather, studded —
  rather than one item class, restore the fifth column first. Doing it afterwards
  means rewriting authored item data, not just the tables.
