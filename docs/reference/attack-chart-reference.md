# Attack chart reference (AT-1 through AT-8)

This is the text and machine-readable reference for the four source images:

- `docs/weaponattack.bmp` — AT-1 through AT-4
- `docs/animalattack.bmp` — CST-2, AT-5, and AT-6
- `docs/spellattack.bmp` — AT-7 and AT-8
- `docs/weapons.bmp` — CST-1

The complete transcription of every attack-chart cell is in
[`merp-attack-tables.csv`](merp-attack-tables.csv). A cell such as `12C` means
12 concussion hits plus a severity-C critical. `F` means a fumble or spell
failure result. `T` is the source chart's Tiny critical grade, below A.

## d20 conversion

The engine uses one random die:

```text
net_roll = raw_d20 + attack_modifier - defence_modifier
chart_roll = 5 * net_roll
```

The net result is not capped at 20. For example, raw d20 18 plus attack modifier
12 and no defence modifier produces net 30, which reads source row 146–150.

Open-ended-up is deliberately excluded. In particular, AT-8 rows 97–99 and 100
are retained in the CSV for reference but are excluded from fitting.

## Source categories

| ID | Chart | Source |
|---|---|---|
| AT-1 | One-handed slashing weapons | `weaponattack.bmp` |
| AT-2 | One-handed concussion weapons | `weaponattack.bmp` |
| AT-3 | Two-handed weapons | `weaponattack.bmp` |
| AT-4 | Missile weapons | `weaponattack.bmp` |
| AT-5 | Tooth & claw | `animalattack.bmp` |
| AT-6 | Grappling & unbalancing | `animalattack.bmp` |
| AT-7 | Bolt spells | `spellattack.bmp` |
| AT-8 | Ball spells | `spellattack.bmp` |

The source has five armour columns: plate, chain, rigid leather, soft leather,
and none. The current engine has four and defines Leather as the arithmetic mean
of the two source leather columns.

## Shape approximation

The damaging-hit thresholds on the integer d20 scale are:

| Table | Plate | Chain | Leather | None |
|---|---:|---:|---:|---:|
| AT-1 | 10 | 11 | 13 | 16 |
| AT-2 | 7 | 9 | 14 | 17 |
| AT-3 | 12 | 14 | 15 | 17 |
| AT-4 | 15 | 16 | 17 | 19 |
| AT-5 | 12 | 13 | 12 | 10 |
| AT-6 | 12 | 14 | 14 | 13 |
| AT-7 | 8 | 9 | 11 | 13 |
| AT-8 | 4 | 5 | 6 | 2 |

The digitized chart is sampled at every integer net d20 result. Failure/fumble
cells are excluded from the damage fit. Shape error is normalized root-mean-square
error (NRMSE), divided by the largest hit result in that armour column. This avoids
undefined percentages at zero and prevents one-hit rounding at the low end from
dominating the measurement.

Use the linear form whenever its NRMSE is at most 5%:

```text
z = max(0, net_roll - origin)
hits = floor(linear * z)
```

AT-5 and AT-6 require the simple convex form in seven of their eight armour
columns:

```text
z = max(0, net_roll - origin)
hits = floor(linear * z + quadratic * z²)
```

| Table | Armour | Origin | Linear | Quadratic | Form | NRMSE |
|---|---:|---:|---:|---:|---|---:|
| AT-1 | Plate | 8 | 0.581 | 0 | linear | 1.5% |
| AT-1 | Chain | 10 | 0.931 | 0 | linear | 1.0% |
| AT-1 | Leather | 11 | 1.251 | 0 | linear | 2.2% |
| AT-1 | None | 13 | 1.808 | 0 | linear | 3.1% |
| AT-2 | Plate | 7 | 0.728 | 0 | linear | 2.0% |
| AT-2 | Chain | 7 | 0.960 | 0 | linear | 0.0% |
| AT-2 | Leather | 11 | 1.071 | 0 | linear | 2.5% |
| AT-2 | None | 13 | 1.403 | 0 | linear | 4.2% |
| AT-3 | Plate | 10 | 1.121 | 0 | linear | 1.2% |
| AT-3 | Chain | 12 | 1.869 | 0 | linear | 0.8% |
| AT-3 | Leather | 13 | 2.429 | 0 | linear | 1.0% |
| AT-3 | None | 14 | 3.010 | 0 | linear | 2.8% |
| AT-4 | Plate | 13 | 0.920 | 0 | linear | 0.0% |
| AT-4 | Chain | 14 | 1.583 | 0 | linear | 1.8% |
| AT-4 | Leather | 14 | 1.673 | 0 | linear | 1.4% |
| AT-4 | None | 16 | 2.003 | 0 | linear | 4.0% |
| AT-5 | Plate | 10 | 0.227 | 0.050 | quadratic | 3.0% |
| AT-5 | Chain | 9 | 0.055 | 0.062 | quadratic | 2.9% |
| AT-5 | Leather | 9 | 0.314 | 0.059 | quadratic | 2.7% |
| AT-5 | None | 7 | 0.336 | 0.062 | quadratic | 2.6% |
| AT-6 | Plate | 10 | 0.151 | 0.035 | quadratic | 3.1% |
| AT-6 | Chain | 12 | 0.355 | 0.050 | quadratic | 2.9% |
| AT-6 | Leather | 12 | 0.553 | 0.056 | quadratic | 2.1% |
| AT-6 | None | 15 | 2.077 | 0 | linear | 3.5% |
| AT-7 | Plate | 5 | 0.698 | 0 | linear | 3.6% |
| AT-7 | Chain | 10 | 1.052 | 0 | linear | 2.0% |
| AT-7 | Leather | 11 | 1.391 | 0 | linear | 2.0% |
| AT-7 | None | 10 | 1.808 | 0 | linear | 3.6% |
| AT-8 | Plate | 1 | 0.939 | 0 | linear | 4.2% |
| AT-8 | Chain | 2 | 0.936 | 0 | linear | 4.2% |
| AT-8 | Leather | 3 | 0.910 | 0 | linear | 4.2% |
| AT-8 | None | 1 | 1.251 | 0 | linear | 0.0% |

Run `py -3 tools/import/analyze_attack_table_shape.py` to reproduce the fit
against the CSV and compare it with the configured runtime data.

## Mechanics that are not damage curves

The critical suffixes are categorical and remain in the CSV rather than being
included in percentage error. The source also contains caps based on animal
attack size (Tiny through Huge) and bolt element (Shock, Water, Ice, Fire, or
Lightning). Those require an attack-size or bolt-type input; a single uncapped
curve cannot reproduce them.

The source fumble/failure cells and AT-8's open-ended-up cells are likewise
separate resolution mechanics, not points on a concussion-hit curve.
