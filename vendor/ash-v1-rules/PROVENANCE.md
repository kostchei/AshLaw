# Vendored Rules Provenance

## Source

- **Source directory:** `D:\Code\ash_v1`
- **Vendored into:** `E:\U8_Ash\vendor\ash-v1-rules`
- **Vendored on:** 27 July 2026
- **Source ownership:** User-provided project material

No external license was present in the source directory. This package is
therefore treated as internal project material; no third-party redistribution
permission is asserted by this manifest.

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

### Known-unvalidated tables

No MERP source table was supplied for AT-7 (Spell Bolts), AT-8 (Spell Balls), or
`environmental_fall_crush`, so none of the three was validated. `environmental_fall_crush`
carries the same reversed-threshold signature as the AT-3/AT-4 defect (Plate 12,
Chain 10, Leather 8, None 6) and should be treated as suspect until checked against a
source table.

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
