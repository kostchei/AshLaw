# Attack chart implementation audit

Audit date: 29 July 2026.

## Confirmed one-d20 resolution

The runtime accepts exactly one raw d20 result (`1..20`) and computes:

```text
net_roll = raw_d20 + attack_modifier - defence_modifier
chart_roll = 5 * net_roll
```

The net result is allowed to exceed 20. A test now verifies that raw 20 plus a
+10 modifier produces net 30, corresponding to source row 146–150, and resolves
AT-3 against no armour as 48 concussion hits.

The same raw d20 also supplies the trauma index (`raw_d20 % 10`, with zero mapped
to 10). No second attack or critical die is rolled. Open-ended-up is intentionally
not implemented.

## Implemented chart shape

AT-1 through AT-8 concussion-hit curves are fitted against every integer net
d20 result represented by the supplied source charts. Every configured column
passes 5% normalized RMSE. Most are linear; seven animal/armour columns use one
quadratic term. See:

- [`attack-chart-reference.md`](attack-chart-reference.md) for formulas and errors;
- [`merp-attack-tables.csv`](merp-attack-tables.csv) for every source cell;
- `tools/import/analyze_attack_table_shape.py` for the reproducible check.

The current runtime has four armour types. Its Leather result is fitted to the
arithmetic mean of the source's Rigid Leather and Soft Leather columns.

## Deferred: environmental fall/crush

`environmental_fall_crush` is the ninth category in the runtime data and the only
one with no source chart anywhere in the supplied material. It is explicitly
deferred: `AttackTable.IsSourceValidated` is false for it, and `AttackResolver`
refuses to resolve against it unless the caller sets
`AttackRequest.AllowUnvalidatedTable`.

It is the only table whose `hit_threshold` equals its `damage_origin` in every
armour column, which is the signature of a table that predates the curve fit. See
[`PROVENANCE.md`](../../vendor/ash-v1-rules/PROVENANCE.md) for the full reasoning
and for what would retire the deferral.

## Source mechanics not currently implemented

These are visible in the reference charts but are not represented by the current
`AttackRequest`/`AttackResolver` contract:

- weapon fumble ranges and weapon-specific fumble consequences;
- source spell-failure cells other than the current natural-1 spell mishap;
- animal attack-size result caps (Tiny, Small, Medium, Large, Huge);
- bolt-type result caps (Shock, Water, Ice, Fire, Lightning);
- Tiny (`T`) critical severity, secondary criticals, and weapon/attack-specific
  critical caps;
- separate Rigid Leather and Soft Leather runtime armour types;
- CST-1 weapon range, loading, OB adjustments, weight, primary/secondary critical
  metadata, and shield/hand restrictions;
- CST-2 animal attack mapping and special fall/hand-to-hand calculations.

Those gaps do not affect the one-d20 confirmation or the fitted concussion-hit
shape, but the BMP content should not be described as fully implemented until
the missing inputs and branches exist.
