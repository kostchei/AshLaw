# Vendored Rules Provenance

## Source

- **Source directory:** `D:\Code\ash_v1`
- **Vendored into:** `E:\U8_Ash\vendor\ash-v1-rules`
- **Vendored on:** 27 July 2026
- **Source ownership:** User-provided project material

No external license was present in the source directory. This package is
therefore treated as internal project material; no third-party redistribution
permission is asserted by this manifest.

> **Open issue — blocks public distribution.** As of 2026-07-29 the repository is
> licensed GPL-2.0
> ([ADR 0005](../../docs/adr/0005-gplv2-and-reuse-over-reimplementation.md)).
> Distributing the repository distributes this directory with it, and this
> directory still has no licence of its own. It is the project owner's own
> material, so it is theirs to license — but it must be licensed explicitly and
> this section updated before any public release.

## Included artifacts

| Source artifact | Destination | Source SHA-256 |
|---|---|---|
| `class_progression_tables.md` | `rules/class_progression_tables.md` | `C395F4BC3B2339613A8A3E19EBBA80EFD1C992AE8800B85BE3FFE5B408387D0C` |
| `combat_engine_rules.md` | `rules/combat_engine_rules.md` | `8AAF2E197BBC8029479EE71A489B4F8C9092BB39164FBBDF0ED91DD517B0A8C9` |
| `critical_hits_system.md` | `rules/critical_hits_system.md` | `FCDC6FF81543495C9BB03E4C395957DF631CC056F0F3EEBE1A20B3C5EF8D3064` |
| `combat_system_data.json` | `data/combat_system_data.json` | `C91A5B33D6834F63722439C8B5B46F4D07A768672A9CADC7BB7569E8356EED8B` |
| `attack_tables_summary.csv` | `data/attack_tables_summary.csv` | `CCB8E887CAB49824667FED72629012BCFEEF879F4FBCD9ECF96CD227E9112AC2` |
| `ct_1_crush_critical_table.csv` | `data/ct_1_crush_critical_table.csv` | `412A0A641C60CE345CFCF449E936B41D2D5B6BBF3F3F6D000C57723E22269EE7` |
| `ct_2_slash_critical_table.csv` | `data/ct_2_slash_critical_table.csv` | `0A30CD75CFF64455019B5CB1EEA0217CDD55BEC9DB9189ABC58ABDF483343770` |
| `ct_3_puncture_critical_table.csv` | `data/ct_3_puncture_critical_table.csv` | `0B45B691EA9DCB84D79E8956887348F5A3EB05B4EA997BAB6CECDA3E3199B3E5` |
| `ct_4_unbalancing_critical_table.csv` | `data/ct_4_unbalancing_critical_table.csv` | `8AFC0E59F8F447968FD0D996B890FC4B12984C2E99EB4BF864CDA6B14F338FF2` |

Hashes identify the source files before the portable-link patch described
below.

## Local vendoring patch

The three Markdown rulebooks contained absolute links such as:

```text
file:///d:/Code/ash_v1/combat_engine_rules.md
```

Those links were changed to portable package-relative links. No formulas,
tables, rule text, JSON values, or CSV values were changed by the vendoring step
itself. Subsequent deliberate corrections are recorded below.

## Post-vendoring corrections

### 2026-08-01 — physical critical mechanics translated source-first

CT-1 through CT-4 retain the MERP injury and only add a 5.5e mastery or
condition when it directly represents an original mechanical effect. Index 5
(MERP 01–05) now has no result for all four physical tables. Small flat damage
awards use Graze only where selected by the conversion; explicit extra hits and
bleeding otherwise remain numeric. Conditional armor and shield clauses remain
conditional.

MERP **stunned** results now map to the 5.5e **Restrained** condition rather
than Incapacitated or Stunned. Prone is used only for an original knockdown;
Incapacitated is reserved for results that actually prevent action; Dying starts
death saves; and the crushed-throat result uses the 5.5e suffocation hazard.
Invented Push, Vex, Slow, Topple, Cleave, and Sap effects were removed where the
source result did not contain an equivalent.

5.5e terminology was checked against the official 2024 D&D Beyond references:
[Mastery Properties](https://www.dndbeyond.com/sources/dnd/br-2024/equipment#MasteryProperties)
and the [Rules Glossary](https://www.dndbeyond.com/sources/dnd/br-2024/rules-glossary).

Regression coverage records the user-specified conversions for indices 5–9 and
16 verbatim, checks the remaining physical upper bands, and verifies that the
new Restrained, Dying, Suffocating, and Push effects are structured data rather
than presentation-only words.

The follow-up conversion replaces physical critical awards of 8–12 immediate
hits with Graze x2 and expresses every physical knockback as Push. Indefinite
-25 activity becomes 2 Exhaustion levels until healed; indefinite -40 activity
becomes Injured with one disadvantaged ability and half Speed; indefinite -50
or -60 activity becomes Injured with two disadvantaged abilities and half
Speed. Results that originally caused unconsciousness for hours instead leave
the target stable at 0 hit points and 0 wounds, waking in 1d4 hours with 1 hit
point and an appropriate one- or two-ability Injury until healed. These replace
the earlier flat-hit and hour-long Unconscious effects rather than stacking with
them.

### 2026-07-31 — physical critical tables restored to MERP bands

The earlier 1–18 refactor used the right lookup shape but populated it with an
authored severity progression that shifted or replaced the published MERP
outcomes. CT-1 through CT-4 now use one shared mapping: indices 1–4 are empty
padding, index 5 begins band 01–05, and indices 11–18 map to 81–86, 87–89,
91–96, 97–99, 101–106, 107–109, 111–116, and 117–119. Exact spectacular
results 80, 90, 100, 110, and 120 remain intentionally omitted from the d20
adaptation.

The injury narratives retain their MERP meaning. Mechanical wording uses 5.5e
weapon masteries and conditions where they are direct equivalents: Graze, Vex,
Sap, Slow, Push, Topple, Cleave, Prone, Incapacitated, Stunned, Unconscious,
Paralyzed, and delayed or instant death. Sap uses its normal next-attack window;
it is not extended for an authored number of rounds.

Changed:

| File | Change |
|---|---|
| `data/ct_1_crush_critical_table.csv` | Restored the MERP band order, including the corrected 81–119 outcomes |
| `data/ct_2_slash_critical_table.csv` | Restored the MERP band order and high-band limb, head, and spine outcomes |
| `data/ct_3_puncture_critical_table.csv` | Restored the MERP band order and high-band lung, head, artery, and kidney outcomes |
| `data/ct_4_unbalancing_critical_table.csv` | Removed grappling results that had leaked into bands 97–99 and 117–119; restored the unbalancing outcomes |
| `rules/critical_hits_system.md` | Documented the shared lookup-to-band mapping and synchronized CT-1 through CT-4 with the runtime CSVs |

Regression coverage asserts all 32 physical outcomes in bands 81–119 and the
empty padding at indices 1–4. A future edit that shifts these rows now fails the
rules test suite.

### 2026-07-27 — AT-3 and AT-4 corrected against published MERP tables

**This package now diverges from its source in `D:\Code\ash_v1`.** A refresh that
blindly copies the source forward will reintroduce the defect described here.

`combat_system_data.json` and `combat_engine_rules.md` carried AT-3 (2-Handed) and
AT-4 (Missile) parameters whose damage curve ran the wrong way against armour:
`hit_threshold` fell as armour got lighter, when the MERP source tables have it rise.
`attack_tables_summary.csv` carried the correct values for both tables, so the package
disagreed with itself on 8 of its 32 attack-table rows. All other tables agreed across
all three files and are unchanged.

The corrections were validated against the published MERP attack tables AT-1 to AT-6
by evaluating the fitted curve at net roll 30 (MERP table row 146-150) and comparing
against the published damage value. Before correction, the JSON's AT-3 predicted 80
hits against an unarmoured target where MERP gives 48, and AT-4 predicted 80 where
MERP gives 27. After correction all 24 validated armour columns fall within 1.5 hits
of source.

Changed:

| File | Change |
|---|---|
| `data/combat_system_data.json` | AT-3 and AT-4 `armor_targets` replaced with the MERP-validated values; both `description` fields rewritten, having described the reversed curve |
| `rules/combat_engine_rules.md` | §4.3 and §4.4 tables and prose updated to match; §5 Scenario 1 recomputed (`7 Hits` -> `8 Hits`, a direct arithmetic consequence of the AT-3 Plate change) |
| `data/attack_tables_summary.csv` | Regenerated from the JSON. Now includes the `environmental_fall_crush` row, which it previously omitted |

`data/attack_tables_summary.csv` is **no longer a hand-maintained source file.** It is
generated from `combat_system_data.json` by `tools/import/gen_attack_tables.py`, which
also validates the fit against `tools/import/merp_reference.json`. Do not edit the CSV
directly; run the generator. This is what prevents the two files diverging again.

### 2026-07-29 — class progression transcribed to CSV

`rules/class_progression_tables.md` §3 publishes the Lv 1–30 attack-modifier curve for
Fighter, Cleric, Rogue, and Wizard as a Markdown table only. It is now also carried as
`data/class_progression.csv`, a **verbatim transcription** of that table — no value was
recomputed, reinterpreted, or filled in.

The Markdown table remains the authority. The CSV exists so the runtime can load the
curve as data rather than hard-coding it, per build plan M0 task 8.

`ClassProgressionTests.BracketRulesDeriveThePublishedTable` independently derives all 120
cells from the §2 bracket rates and asserts they reproduce the CSV exactly, so a
transcription error in either direction fails the build. The derivation also reproduces
the four cap-reached levels quoted in §2 and §4 (Fighter 26, Cleric 29, Rogue 26,
Wizard 19).

One derivation detail is worth recording because it is not stated in §1: no single
rounding mode reproduces the published table. The `+2/3` rate grants its first point on
the **opening** level of a bracket, whereas `+1/2` and `+1/3` grant theirs on the
**closing** level of each period. The rates are therefore encoded as explicit per-period
gain patterns (`+1/+0/+1`, `+0/+1`, `+0/+0/+1`) rather than as rounded arithmetic on
`levels × rate`.

### Deferred table — `environmental_fall_crush`

`environmental_fall_crush` has no supplied source table, and none exists in the
material to hand: `docs/reference/merp-attack-tables.csv` transcribes AT-1 through
AT-8 only, and the supplied BMP charts carry no environmental table. It therefore
**cannot be sourced, and is explicitly deferred** rather than accepted.

Deferred means enforced, not merely noted. `AttackTable.IsSourceValidated` is false
for this category alone, and `AttackResolver.Resolve` throws unless the caller sets
`AttackRequest.AllowUnvalidatedTable`. Nothing can consume these numbers by accident
as though they were sourced. `UnvalidatedTableTests` holds this in place.

**Correction to the earlier note.** A previous revision of this section claimed the
table "carries the same reversed-threshold signature as the earlier AT-3/AT-4
defect". That reasoning does not hold. A hit threshold that falls from Plate to None
is not by itself a defect signature: AT-5 (12/13/12/10) and AT-8 (4/5/6/2) both fall
and both are validated against the source charts, which show damage beginning
earliest against unarmoured targets for those two tables. Direction alone proves
nothing.

The evidence that the table was never fitted is different and specific:
`environmental_fall_crush` is the **only** table whose `hit_threshold` equals its
`damage_origin` in every armour column (12/12, 10/10, 8/8, 6/6). Separating those two
fields was the whole point of the 2026-07-29 curve fit; all eight fitted tables
separate them. This one retains its pre-fit shape, so its damage curve, hit
threshold, and critical thresholds are all unverified authored values.

To retire the deferral, supply an environmental source table, extend
`docs/reference/merp-attack-tables.csv` and `analyze_attack_table_shape.py` to cover
it, then remove the category from `UnsourcedCategories` in `RulesDataLoader`.

### 2026-07-29 — full AT-1 through AT-8 chart transcription and curve fit

The supplied BMP charts were transcribed to
`docs/reference/merp-attack-tables.csv`. AT-1 through AT-8 are now checked at every
integer d20-scale net result by `tools/import/analyze_attack_table_shape.py`.

The damage contract gained separate `damage_origin` and `quadratic` fields. A linear
curve is retained where it meets the 5% normalized-RMSE target; seven AT-5/AT-6
columns use a small quadratic term. All configured AT-1 through AT-8 columns pass.
AT-8 open-ended-up rows 97–100 are retained as reference data but excluded from the
fit by design.

### 2026-07-29 — inconsistent squid worked example retired

`rules/combat_engine_rules.md` §5 Scenario 2 quoted attack parameters and trauma text
that exist in no runtime table. The scenario is now explicitly marked retired and must
not be used as a fixture. A corrected illustration using the likely intended AT-6
Leather row and the authoritative CT-4 CSV outcome is recorded in its place.

## Excluded artifacts

| Excluded artifact | Reason |
|---|---|
| `ash_v1_gdrive_docs.zip` | Duplicate transport archive containing four included source documents |
| `roblox_sync.bat` | Operational synchronization utility, not rules content |
| `upload_playwright.py` | Google Drive upload automation, not rules content |
| `upload_to_gdrive.py` | Google Drive upload automation, not rules content |

## Refresh procedure

To refresh the vendor package:

1. Compare the source artifacts against the hashes above.
2. Review source changes as rules changes, not as an automatic overwrite.
3. Copy the nine included artifacts into the same destination paths.
4. Reapply portable relative links.
5. Revalidate JSON and all CSV files.
6. Update this manifest with the new source hashes and date.
7. Review `INTEGRATION.md` for contract changes.
